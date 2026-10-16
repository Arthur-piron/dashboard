using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class WidgetInstanceTests : IClassFixture<ApiFactoryWithStubs>, IAsyncLifetime
{
    private readonly ApiFactoryWithStubs _factory;
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public WidgetInstanceTests(ApiFactoryWithStubs factory)
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

    private static object Weather(string city = "Lille") => new { city };

    private Task<HttpResponseMessage> CreateAsync(
        object? config = null,
        string widgetKey = "weather.current",
        int refreshRateSeconds = 60,
        object? position = null,
        HttpClient? client = null) =>
        (client ?? _api).PostAsJsonAsync("/widget-instances", new
        {
            widgetKey,
            config = config ?? Weather(),
            refreshRateSeconds,
            position,
        });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<int> CreateIdAsync(object? config = null, int refreshRateSeconds = 60)
    {
        var response = await CreateAsync(config, refreshRateSeconds: refreshRateSeconds);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    private async Task<HttpClient> NewUserClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await TestUser.CreateAndLoginAsync(client, _mailpit);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Widget_instances_require_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/widget-instances");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_instance_returns_it_with_its_service_and_defaults()
    {
        var response = await CreateAsync(Weather("Tokyo"), refreshRateSeconds: 120);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal("weather.current", body.GetProperty("widgetKey").GetString());
        Assert.Equal("weather", body.GetProperty("serviceKey").GetString());
        Assert.Equal("Tokyo", body.GetProperty("config").GetProperty("city").GetString());
        Assert.Equal(120, body.GetProperty("refreshRateSeconds").GetInt32());

        var position = body.GetProperty("position");
        Assert.Equal(0, position.GetProperty("x").GetInt32());
        Assert.Equal(1, position.GetProperty("width").GetInt32());

        Assert.Equal($"/widget-instances/{body.GetProperty("id").GetInt32()}",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Two_instances_of_the_same_widget_keep_their_own_configuration()
    {
        await CreateAsync(Weather("Lille"));
        await CreateAsync(Weather("Tokyo"));

        var list = await JsonAsync(await _api.GetAsync("/widget-instances"));
        var cities = list.EnumerateArray()
            .Select(i => i.GetProperty("config").GetProperty("city").GetString())
            .ToList();

        Assert.Equal(new[] { "Lille", "Tokyo" }, cities);
    }

    [Fact]
    public async Task Creating_with_an_unknown_widget_is_refused()
    {
        var response = await CreateAsync(widgetKey: "nope.nope");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown widget", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creating_with_an_invalid_config_is_refused()
    {
        var response = await CreateAsync(new { town = "Lille" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("'city' is required", body);
        Assert.Contains("'town' is not a parameter", body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100000)]
    public async Task Creating_with_a_refresh_rate_out_of_range_is_refused(int seconds)
    {
        var response = await CreateAsync(refreshRateSeconds: seconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("refreshRateSeconds", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 99)]
    public async Task Creating_with_an_invalid_position_is_refused(int x, int y, int width, int height)
    {
        var response = await CreateAsync(position: new { x, y, width, height });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("position", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creating_a_widget_of_an_unsubscribed_service_is_refused()
    {
        var response = await CreateAsync(new { label = "hi" }, widgetKey: "stub-apikey.echo");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reconfiguring_changes_the_config_and_the_refresh_rate()
    {
        var id = await CreateIdAsync(Weather("Lille"));

        var response = await _api.PutAsJsonAsync($"/widget-instances/{id}",
            new { config = Weather("Paris"), refreshRateSeconds = 900 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("Paris", body.GetProperty("config").GetProperty("city").GetString());
        Assert.Equal(900, body.GetProperty("refreshRateSeconds").GetInt32());
    }

    [Fact]
    public async Task Reconfiguring_with_an_invalid_config_is_refused()
    {
        var id = await CreateIdAsync();

        var response = await _api.PutAsJsonAsync($"/widget-instances/{id}",
            new { config = new { city = "Lille", unit = "kelvin" }, refreshRateSeconds = 60 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be one of", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Moving_changes_only_the_position()
    {
        var id = await CreateIdAsync(Weather("Lille"));

        var response = await _api.PatchAsJsonAsync($"/widget-instances/{id}/position",
            new { x = 2, y = 3, width = 4, height = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        var position = body.GetProperty("position");
        Assert.Equal(2, position.GetProperty("x").GetInt32());
        Assert.Equal(3, position.GetProperty("y").GetInt32());
        Assert.Equal(4, position.GetProperty("width").GetInt32());
        Assert.Equal(2, position.GetProperty("height").GetInt32());
        Assert.Equal("Lille", body.GetProperty("config").GetProperty("city").GetString());
    }

    [Fact]
    public async Task Deleting_removes_the_instance()
    {
        var id = await CreateIdAsync();

        var deleted = await _api.DeleteAsync($"/widget-instances/{id}");
        var afterwards = await _api.GetAsync($"/widget-instances/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task Deleting_twice_returns_404()
    {
        var id = await CreateIdAsync();
        await _api.DeleteAsync($"/widget-instances/{id}");

        var response = await _api.DeleteAsync($"/widget-instances/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_instance_is_not_found_rather_than_forbidden()
    {
        var id = await CreateIdAsync();
        var other = await NewUserClientAsync();

        // 404 everywhere, so the existence of the id is never revealed.
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/widget-instances/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/widget-instances/{id}",
                new { config = Weather("Berlin"), refreshRateSeconds = 60 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PatchAsJsonAsync($"/widget-instances/{id}/position",
                new { x = 9, y = 9, width = 1, height = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/widget-instances/{id}")).StatusCode);

        // ...and the owner's instance is untouched.
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync($"/widget-instances/{id}")).StatusCode);
        Assert.Empty((await JsonAsync(await other.GetAsync("/widget-instances"))).EnumerateArray());
    }

    [Fact]
    public async Task The_list_only_contains_the_callers_instances()
    {
        await CreateAsync(Weather("Lille"));
        var other = await NewUserClientAsync();
        await CreateAsync(Weather("Tokyo"), client: other);

        var mine = await JsonAsync(await _api.GetAsync("/widget-instances"));
        var theirs = await JsonAsync(await other.GetAsync("/widget-instances"));

        Assert.Single(mine.EnumerateArray());
        Assert.Single(theirs.EnumerateArray());
        Assert.Equal("Lille", mine.EnumerateArray().Single().GetProperty("config").GetProperty("city").GetString());
        Assert.Equal("Tokyo", theirs.EnumerateArray().Single().GetProperty("config").GetProperty("city").GetString());
    }
}
