using System.Net;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class AboutTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _api;

    public AboutTests(ApiFactory factory)
    {
        _api = factory.CreateClient();
    }

    private async Task<JsonElement> GetAboutAsync(HttpRequestMessage? request = null)
    {
        var response = request is null
            ? await _api.GetAsync("/about.json")
            : await _api.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Is_reachable_without_a_token()
    {
        var response = await _api.GetAsync("/about.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reports_the_client_address_from_the_forwarded_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/about.json");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        var about = await GetAboutAsync(request);

        Assert.Equal("203.0.113.7", about.GetProperty("client").GetProperty("host").GetString());
    }

    [Fact]
    public async Task Reports_the_server_time_as_a_unix_timestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var about = await GetAboutAsync();
        var currentTime = about.GetProperty("server").GetProperty("current_time").GetInt64();

        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(currentTime, before, after);
    }

    [Fact]
    public async Task Lists_every_service_with_its_widgets()
    {
        var about = await GetAboutAsync();

        var services = about.GetProperty("server").GetProperty("services").EnumerateArray().ToList();
        Assert.NotEmpty(services);

        var weather = services.Single(s => s.GetProperty("name").GetString() == "weather");
        var widgetNames = weather.GetProperty("widgets").EnumerateArray()
            .Select(w => w.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("weather.current", widgetNames);
        Assert.Contains("weather.forecast", widgetNames);
    }

    [Fact]
    public async Task Every_widget_declares_at_least_one_parameter()
    {
        var about = await GetAboutAsync();

        foreach (var widget in AllWidgets(about))
        {
            var count = widget.GetProperty("params").GetArrayLength();
            Assert.True(count > 0, $"Widget '{widget.GetProperty("name")}' has no parameter, which the subject forbids.");
        }
    }

    [Fact]
    public async Task Every_parameter_has_a_name_and_a_supported_type()
    {
        var about = await GetAboutAsync();

        foreach (var widget in AllWidgets(about))
        {
            foreach (var param in widget.GetProperty("params").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(param.GetProperty("name").GetString()));
                Assert.Contains(param.GetProperty("type").GetString(), new[] { "integer", "string" });
            }
        }
    }

    private static IEnumerable<JsonElement> AllWidgets(JsonElement about) =>
        about.GetProperty("server").GetProperty("services").EnumerateArray()
            .SelectMany(s => s.GetProperty("widgets").EnumerateArray());
}
