using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class CatalogTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public CatalogTests(ApiFactory factory)
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

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await _api.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Theory]
    [InlineData("/services")]
    [InlineData("/widgets")]
    [InlineData("/widgets/weather.current/preview?city=Lille")]
    public async Task Catalog_requires_a_token(string url)
    {
        // A client from the factory, without the Authorization header set in InitializeAsync.
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Services_describes_weather_as_needing_no_account()
    {
        var services = await GetJsonAsync("/services");

        var weather = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "weather");
        Assert.Equal("Weather", weather.GetProperty("name").GetString());
        Assert.Equal("none", weather.GetProperty("auth").GetString());
        Assert.True(weather.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Widgets_expose_the_metadata_the_front_end_needs_to_build_a_form()
    {
        var widgets = await GetJsonAsync("/widgets");

        var forecast = widgets.EnumerateArray().Single(w => w.GetProperty("key").GetString() == "weather.forecast");
        Assert.Equal("weather", forecast.GetProperty("serviceKey").GetString());
        Assert.True(forecast.GetProperty("available").GetBoolean());

        var parameters = forecast.GetProperty("params").EnumerateArray().ToList();

        var city = parameters.Single(p => p.GetProperty("name").GetString() == "city");
        Assert.Equal("string", city.GetProperty("type").GetString());
        Assert.True(city.GetProperty("required").GetBoolean());

        var days = parameters.Single(p => p.GetProperty("name").GetString() == "days");
        Assert.Equal("integer", days.GetProperty("type").GetString());
        Assert.Equal(1, days.GetProperty("min").GetInt32());
        Assert.Equal(7, days.GetProperty("max").GetInt32());

        var unit = parameters.Single(p => p.GetProperty("name").GetString() == "unit");
        var options = unit.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.Equal(new[] { "celsius", "fahrenheit" }, options);
    }

    [Fact]
    public async Task Preview_returns_current_weather_for_a_known_city()
    {
        var data = await GetJsonAsync("/widgets/weather.current/preview?city=Lille");

        Assert.Equal("Lille", data.GetProperty("city").GetString());
        Assert.Equal("France", data.GetProperty("country").GetString());
        Assert.Equal("celsius", data.GetProperty("unit").GetString());
        Assert.Equal("Partly cloudy", data.GetProperty("sky").GetString());
        Assert.Equal(19.3, data.GetProperty("temperature").GetDouble(), 1);
    }

    [Fact]
    public async Task Preview_honours_integer_and_option_parameters()
    {
        var data = await GetJsonAsync("/widgets/weather.forecast/preview?city=Tokyo&days=5&unit=fahrenheit");

        Assert.Equal("Tokyo", data.GetProperty("city").GetString());
        Assert.Equal("fahrenheit", data.GetProperty("unit").GetString());
        Assert.Equal(5, data.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task Preview_without_a_required_parameter_returns_400()
    {
        var response = await _api.GetAsync("/widgets/weather.current/preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'city' is required", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("days=12", "must be <= 7")]
    [InlineData("days=notanumber", "must be an integer")]
    [InlineData("unit=kelvin", "must be one of")]
    [InlineData("bogus=1", "is not a parameter")]
    public async Task Preview_rejects_an_invalid_config(string parameter, string expectedMessage)
    {
        var response = await _api.GetAsync($"/widgets/weather.forecast/preview?city=Lille&{parameter}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_of_an_unknown_city_returns_422()
    {
        var response = await _api.GetAsync("/widgets/weather.current/preview?city=Xyzzyville");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Unknown city", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Preview_of_an_unknown_widget_returns_404()
    {
        var response = await _api.GetAsync("/widgets/nope/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
