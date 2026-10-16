using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class WidgetTimerTests : IClassFixture<ApiFactoryWithStubs>, IAsyncLifetime
{
    private readonly ApiFactoryWithStubs _factory;
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public WidgetTimerTests(ApiFactoryWithStubs factory)
    {
        _factory = factory;
        _api = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<int> CreateAsync(
        object config, string widgetKey = "weather.current", int refreshRateSeconds = 60, HttpClient? api = null)
    {
        var response = await (api ?? _api).PostAsJsonAsync("/widget-instances",
            new { widgetKey, config, refreshRateSeconds });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    // The timer ticks once a second; poll until it has produced something.
    private async Task<JsonElement> WaitForDataAsync(int id, string expectedStatus = "ok", HttpClient? api = null)
    {
        api ??= _api;
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var data = await JsonAsync(await api.GetAsync($"/widget-instances/{id}/data"));
            if (data.GetProperty("status").GetString() == expectedStatus) return data;
            await Task.Delay(200);
        }

        var last = await JsonAsync(await api.GetAsync($"/widget-instances/{id}/data"));
        throw new TimeoutException(
            $"Instance {id} never reached '{expectedStatus}'. Last status: {last.GetProperty("status")}.");
    }

    [Fact]
    public async Task Data_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/widget-instances/1/data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_fetches_again_right_away_and_returns_the_new_result()
    {
        await using var noCooldown = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Timer:ManualRefreshCooldownSeconds", "0"));
        using var api = noCooldown.CreateClient();
        api.DefaultRequestHeaders.Authorization = _api.DefaultRequestHeaders.Authorization;

        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600, api: api);
        var first = await WaitForDataAsync(id, api: api);

        var response = await api.PostAsync($"/widget-instances/{id}/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await JsonAsync(response);
        Assert.Equal("ok", refreshed.GetProperty("status").GetString());
        Assert.True(refreshed.GetProperty("fetchedAt").GetDateTime() > first.GetProperty("fetchedAt").GetDateTime());
    }

    [Fact]
    public async Task Refresh_inside_the_cooldown_returns_the_cached_record_without_fetching()
    {
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);
        var first = await WaitForDataAsync(id);

        var response = await _api.PostAsync($"/widget-instances/{id}/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await JsonAsync(response);
        Assert.Equal("ok", refreshed.GetProperty("status").GetString());
        Assert.Equal(first.GetProperty("fetchedAt").GetDateTime(), refreshed.GetProperty("fetchedAt").GetDateTime());
    }

    [Fact]
    public async Task Refresh_of_an_unknown_instance_is_404()
    {
        var response = await _api.PostAsync("/widget-instances/999999/refresh", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_new_instance_is_refreshed_without_waiting_a_full_period()
    {
        // An hour's refresh rate: only the "due immediately on creation" rule can satisfy this.
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);

        var data = await WaitForDataAsync(id);

        Assert.Equal("weather.current", data.GetProperty("widgetKey").GetString());
        Assert.Equal("Lille", data.GetProperty("data").GetProperty("city").GetString());
        Assert.Equal(19.3, data.GetProperty("data").GetProperty("temperature").GetDouble(), 1);
        Assert.Null(data.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_result_carries_when_it_was_fetched_and_when_it_is_due_again()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 600);

        var data = await WaitForDataAsync(id);

        var fetchedAt = data.GetProperty("fetchedAt").GetDateTime();
        var nextRefreshAt = data.GetProperty("nextRefreshAt").GetDateTime();

        Assert.InRange(fetchedAt, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(600, (nextRefreshAt - fetchedAt).TotalSeconds, 1);
    }

    [Fact]
    public async Task An_instance_that_has_not_been_fetched_yet_reads_as_pending()
    {
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);

        // Immediately after creation the tick may not have run yet; either state is valid,
        // but the shape must be the documented one in both cases.
        var data = await JsonAsync(await _api.GetAsync($"/widget-instances/{id}/data"));
        var status = data.GetProperty("status").GetString();

        Assert.Contains(status, new[] { "pending", "ok" });
        Assert.Equal(id, data.GetProperty("instanceId").GetInt32());
        if (status == "pending")
        {
            Assert.Equal(JsonValueKind.Null, data.GetProperty("data").ValueKind);
            Assert.Equal(JsonValueKind.Null, data.GetProperty("fetchedAt").ValueKind);
        }
    }

    [Fact]
    public async Task A_widget_whose_provider_rejects_the_config_reports_the_reason()
    {
        var id = await CreateAsync(new { city = "Xyzzyville" }, refreshRateSeconds: 3600);

        var data = await WaitForDataAsync(id, expectedStatus: "error");

        Assert.Contains("Unknown city", data.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Reconfiguring_refetches_with_the_new_configuration()
    {
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);
        await WaitForDataAsync(id);

        var update = await _api.PutAsJsonAsync($"/widget-instances/{id}",
            new { config = new { city = "Tokyo" }, refreshRateSeconds = 3600 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // Without the timer being told, the hourly rate would keep serving Lille.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var data = await JsonAsync(await _api.GetAsync($"/widget-instances/{id}/data"));
            if (data.GetProperty("status").GetString() == "ok"
                && data.GetProperty("data").GetProperty("city").GetString() == "Tokyo")
            {
                return;
            }
            await Task.Delay(200);
        }

        Assert.Fail("The instance kept serving the old configuration.");
    }

    [Fact]
    public async Task Deleting_an_instance_stops_it_being_reported()
    {
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);
        await WaitForDataAsync(id);

        await _api.DeleteAsync($"/widget-instances/{id}");

        var response = await _api.GetAsync($"/widget-instances/{id}/data");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var all = await JsonAsync(await _api.GetAsync("/widget-instances/data"));
        Assert.DoesNotContain(all.EnumerateArray(), r => r.GetProperty("instanceId").GetInt32() == id);
    }

    [Fact]
    public async Task The_whole_dashboard_can_be_read_in_one_call()
    {
        var lille = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);
        var tokyo = await CreateAsync(new { city = "Tokyo" }, refreshRateSeconds: 3600);
        await WaitForDataAsync(lille);
        await WaitForDataAsync(tokyo);

        var all = await JsonAsync(await _api.GetAsync("/widget-instances/data"));
        var mine = all.EnumerateArray().ToList();

        Assert.Contains(mine, r => r.GetProperty("instanceId").GetInt32() == lille);
        Assert.Contains(mine, r => r.GetProperty("instanceId").GetInt32() == tokyo);
        Assert.All(mine, r => Assert.Contains(r.GetProperty("status").GetString(), new[] { "ok", "pending", "error" }));
    }

    [Fact]
    public async Task Another_users_data_is_not_reachable()
    {
        var id = await CreateAsync(new { city = "Lille" }, refreshRateSeconds: 3600);
        await WaitForDataAsync(id);

        var other = _factory.CreateClient();
        var token = await TestUser.CreateAndLoginAsync(other, _mailpit);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/widget-instances/{id}/data")).StatusCode);

        var theirs = await JsonAsync(await other.GetAsync("/widget-instances/data"));
        Assert.DoesNotContain(theirs.EnumerateArray(), r => r.GetProperty("instanceId").GetInt32() == id);
    }
}
