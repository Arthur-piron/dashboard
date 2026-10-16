using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class DuolingoWidgetTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public DuolingoWidgetTests(ApiFactory factory)
    {
        _api = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> PreviewAsync(string widget, string query)
    {
        var response = await _api.GetAsync($"/widgets/{widget}/preview?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Duolingo_needs_no_account()
    {
        var response = await _api.GetAsync("/services");
        var services = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var duolingo = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "duolingo");
        Assert.Equal("none", duolingo.GetProperty("auth").GetString());
        Assert.True(duolingo.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Profile_returns_streak_and_total_xp()
    {
        var data = await PreviewAsync("duolingo.profile", "username=test");

        Assert.Equal("test", data.GetProperty("username").GetString());
        Assert.Equal("john", data.GetProperty("name").GetString());
        Assert.Equal(7, data.GetProperty("streak").GetInt32());
        Assert.Equal(5643, data.GetProperty("totalXp").GetInt32());
        Assert.Equal("es", data.GetProperty("learningLanguage").GetString());
        Assert.Equal(2, data.GetProperty("courseCount").GetInt32());
    }

    [Fact]
    public async Task Profile_without_a_username_is_refused()
    {
        var response = await _api.GetAsync("/widgets/duolingo.profile/preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'username' is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Profile_of_an_unknown_user_returns_422()
    {
        var response = await _api.GetAsync("/widgets/duolingo.profile/preview?username=nobody-here");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("No public Duolingo profile", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Course_defaults_to_the_language_being_learnt()
    {
        var data = await PreviewAsync("duolingo.course", "username=test");

        Assert.Equal("Spanish", data.GetProperty("title").GetString());
        Assert.Equal("es", data.GetProperty("learningLanguage").GetString());
        Assert.Equal(5000, data.GetProperty("xp").GetInt32());
        Assert.Equal(42, data.GetProperty("crowns").GetInt32());
        Assert.True(data.GetProperty("isCurrentCourse").GetBoolean());
    }

    [Fact]
    public async Task Course_can_be_pointed_at_another_language()
    {
        var data = await PreviewAsync("duolingo.course", "username=test&language=ja");

        Assert.Equal("Japanese", data.GetProperty("title").GetString());
        Assert.Equal(643, data.GetProperty("xp").GetInt32());
        Assert.False(data.GetProperty("isCurrentCourse").GetBoolean());
    }

    [Fact]
    public async Task Course_for_a_language_the_user_does_not_learn_returns_422_and_says_which_exist()
    {
        var response = await _api.GetAsync("/widgets/duolingo.course/preview?username=test&language=it");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("is not learning", body);
        Assert.Contains("es", body);
        Assert.Contains("ja", body);
    }

    [Fact]
    public async Task Course_for_a_user_with_no_public_course_returns_422()
    {
        var response = await _api.GetAsync("/widgets/duolingo.course/preview?username=solo");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("no public course", await response.Content.ReadAsStringAsync());
    }
}
