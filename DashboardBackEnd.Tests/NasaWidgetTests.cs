using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DashboardBackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

// NASA is the one real service subscribed to with a pasted key, so these tests cover the whole
// chain: prompt, validation at subscribe time, encrypted storage, and the key reaching FetchAsync.
public class NasaWidgetTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public NasaWidgetTests(ApiFactory factory)
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

    private Task<HttpResponseMessage> SubscribeAsync(string apiKey) =>
        _api.PostAsJsonAsync("/subscriptions/nasa", new { apiKey });

    private async Task<JsonElement> PreviewAsync(string widget, string query = "")
    {
        var response = await _api.GetAsync($"/widgets/{widget}/preview?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    [Fact]
    public async Task Nasa_asks_for_the_users_own_key_without_suggesting_one()
    {
        var services = await JsonAsync(await _api.GetAsync("/services"));

        var nasa = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "nasa");
        Assert.Equal("apiKey", nasa.GetProperty("auth").GetString());
        Assert.False(nasa.GetProperty("subscribed").GetBoolean());

        var prompt = nasa.GetProperty("credentialPrompt");
        Assert.Equal("API key", prompt.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, prompt.GetProperty("suggestedValue").ValueKind);
        Assert.Equal("https://api.nasa.gov", prompt.GetProperty("signupUrl").GetString());
    }

    [Fact]
    public async Task Its_widgets_are_refused_until_the_user_subscribes()
    {
        var response = await _api.GetAsync("/widgets/nasa.apod/preview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_key_nasa_rejects_is_never_stored()
    {
        var response = await SubscribeAsync("not-a-real-key");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var services = await JsonAsync(await _api.GetAsync("/services"));
        var nasa = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "nasa");
        Assert.False(nasa.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task A_rate_limited_key_check_is_not_reported_as_a_rejected_key()
    {
        var response = await SubscribeAsync(FakeNasaHandler.ExhaustedKey);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Contains("rate limit", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_valid_key_subscribes_and_is_stored_encrypted()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await SubscribeAsync(FakeNasaHandler.ValidKey)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ServiceSubscriptions
            .Where(s => s.ServiceKey == "nasa")
            .Select(s => s.ProtectedCredentials)
            .ToListAsync();

        Assert.NotEmpty(stored);
        Assert.All(stored, value => Assert.DoesNotContain(FakeNasaHandler.ValidKey, value));
    }

    [Fact]
    public async Task Apod_returns_the_picture_of_the_day()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var data = await PreviewAsync("nasa.apod");

        Assert.Equal(FakeNasaHandler.KnownDate, data.GetProperty("date").GetString());
        Assert.Equal("Pillars of Creation", data.GetProperty("title").GetString());
        Assert.Equal("image", data.GetProperty("mediaType").GetString());
        Assert.StartsWith("https://apod.nasa.gov/", data.GetProperty("url").GetString());
        Assert.Equal("NASA, ESA, CSA, STScI", data.GetProperty("copyright").GetString());
    }

    [Fact]
    public async Task Apod_takes_the_real_picture_from_science_nasa_gov_when_the_api_sends_the_logo()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var apod = await PreviewAsync("nasa.apod", $"date={FakeNasaHandler.PlaceholderDate}");

        Assert.Equal("M104: The Sombrero Galaxy\u2019s Tidal Streams", apod.GetProperty("title").GetString());
        Assert.Equal("A deep image of the Sombrero galaxy reveals surprises.", apod.GetProperty("explanation").GetString());
        Assert.Equal(
            "https://assets.science.nasa.gov/dynamicimage/assets/science/cds/apod/apod/2026/october/M104.jpg?w=4222&h=2817",
            apod.GetProperty("url").GetString());
        Assert.Equal(
            "https://assets.science.nasa.gov/dynamicimage/assets/science/cds/apod/apod/2026/october/M104.jpg",
            apod.GetProperty("hdUrl").GetString());
        Assert.Equal(JsonValueKind.Null, apod.GetProperty("copyright").ValueKind);
    }

    [Fact]
    public async Task Apod_can_be_pointed_at_a_past_day()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var data = await PreviewAsync("nasa.apod", "date=2020-01-02");

        Assert.Equal("2020-01-02", data.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Apod_passes_nasas_own_wording_for_a_date_it_refuses()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var response = await _api.GetAsync($"/widgets/nasa.apod/preview?date={FakeNasaHandler.DateWithoutPicture}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Date must be between", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mars_returns_the_latest_photos_of_a_rover()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var data = await PreviewAsync("nasa.mars", "rover=curiosity&count=2");

        Assert.Equal("Curiosity", data.GetProperty("rover").GetString());
        Assert.Equal("active", data.GetProperty("status").GetString());
        Assert.Equal(4102, data.GetProperty("sol").GetInt32());

        var photos = data.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        Assert.Equal("Front Hazard Avoidance Camera", photos[0].GetProperty("camera").GetString());
        Assert.StartsWith("https://mars.nasa.gov/", photos[0].GetProperty("imageUrl").GetString());
    }

    [Fact]
    public async Task Mars_defaults_to_three_photos()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var data = await PreviewAsync("nasa.mars", "rover=curiosity");

        Assert.Equal(3, data.GetProperty("photos").GetArrayLength());
    }

    [Fact]
    public async Task Mars_refuses_a_rover_that_is_not_one_of_the_options()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var response = await _api.GetAsync("/widgets/nasa.mars/preview?rover=sojourner");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be one of", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mars_says_so_when_a_rover_has_published_nothing()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        var response = await _api.GetAsync($"/widgets/nasa.mars/preview?rover={FakeNasaHandler.RoverWithoutPhotos}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("no published photo", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_key_out_of_quota_is_reported_as_a_rate_limit_not_an_error()
    {
        await SubscribeAsync(FakeNasaHandler.ThrottledKey);

        var response = await _api.GetAsync("/widgets/nasa.mars/preview?rover=curiosity");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("rate limit", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_stored_key_never_comes_back_out_of_the_api()
    {
        await SubscribeAsync(FakeNasaHandler.ValidKey);

        foreach (var url in new[] { "/subscriptions", "/services", "/widgets", "/widgets/nasa.apod/preview" })
        {
            var body = await (await _api.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(FakeNasaHandler.ValidKey, body);
        }
    }
}
