using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DashboardBackEnd.Subscriptions;
using DashboardBackEnd.Widgets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

// GitHub is the one service whose credential we obtain ourselves rather than being pasted, so these
// tests cover the chain from a linked account to a widget payload: the OAuth token reaching
// FetchAsync, the parameters, and the two failures only this service has — a revoked link and an
// exhausted rate limit.
public class GitHubWidgetTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Profile = "github.profile";
    private const string Repos = "github.repos";
    private const string Activity = "github.activity";

    private readonly ApiFactory _factory;
    private readonly HttpClient _api;
    private readonly HttpClient _browser;
    private readonly MailpitClient _mailpit = new();

    private string _userId = "";

    public GitHubWidgetTests(ApiFactory factory)
    {
        _factory = factory;
        _api = factory.CreateClient();

        // GitHub redirects a browser that carries no JWT and must not follow our redirect back.
        _browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _userId = (await JsonAsync(await _api.GetAsync("/auth/me"))).GetProperty("id").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private Task<HttpResponseMessage> PreviewAsync(string widget, string query = "") =>
        _api.GetAsync($"/widgets/{widget}/preview?{query}");

    private async Task<JsonElement> DataAsync(string widget, string query = "")
    {
        var response = await PreviewAsync(widget, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private Task<string> LinkAsync(string? login = null) => GitHubLink.LinkAsync(_api, _browser, login);

    // The only way to hold a token GitHub refuses: the subscription endpoint will not take one for
    // an OAuth service, so this writes it the way the callback does.
    private async Task StoreTokenAsync(string accessToken)
    {
        using var scope = _factory.Services.CreateScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

        await subscriptions.SubscribeAsync(_userId, "github", new ServiceCredentials(AccessToken: accessToken));
    }

    [Fact]
    public async Task The_github_service_now_offers_three_widgets()
    {
        var about = await JsonAsync(await _api.GetAsync("/about.json"));

        var github = about.GetProperty("server").GetProperty("services").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "github");

        var keys = github.GetProperty("widgets").EnumerateArray()
            .Select(w => w.GetProperty("name").GetString())
            .ToList();

        Assert.Equal([Activity, Profile, Repos], keys);
    }

    [Fact]
    public async Task Its_widgets_are_listed_as_unavailable_until_the_account_is_linked()
    {
        var widgets = await JsonAsync(await _api.GetAsync("/widgets"));

        var github = widgets.EnumerateArray()
            .Where(w => w.GetProperty("serviceKey").GetString() == "github")
            .ToList();

        Assert.Equal(3, github.Count);
        Assert.All(github, w => Assert.False(w.GetProperty("available").GetBoolean()));
        Assert.All(github, w => Assert.NotEmpty(w.GetProperty("params").EnumerateArray()));
    }

    [Theory]
    [InlineData(Profile)]
    [InlineData(Repos)]
    [InlineData(Activity)]
    public async Task Its_widgets_are_refused_until_the_account_is_linked(string widget)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(widget)).StatusCode);
    }

    [Fact]
    public async Task The_profile_widget_describes_the_linked_account()
    {
        var login = await LinkAsync();

        var data = await DataAsync(Profile);

        Assert.Equal(login, data.GetProperty("login").GetString());
        Assert.Equal("Test Account", data.GetProperty("name").GetString());
        Assert.Equal($"https://github.com/{login}", data.GetProperty("htmlUrl").GetString());
        Assert.Equal(FakeGitHubHandler.PublicRepos, data.GetProperty("publicRepos").GetInt32());
        Assert.Equal(FakeGitHubHandler.Followers, data.GetProperty("followers").GetInt32());
    }

    [Fact]
    public async Task The_profile_is_compact_unless_the_full_detail_is_asked_for()
    {
        await LinkAsync();

        var compact = await DataAsync(Profile);
        Assert.Equal("compact", compact.GetProperty("detail").GetString());
        foreach (var field in new[] { "bio", "company", "location", "createdAt" })
        {
            Assert.False(compact.TryGetProperty(field, out _), $"compact should not carry '{field}'.");
        }

        var full = await DataAsync(Profile, "detail=full");
        Assert.Equal("full", full.GetProperty("detail").GetString());
        Assert.Equal("Built by a test", full.GetProperty("bio").GetString());
        Assert.Equal("Lille", full.GetProperty("location").GetString());
        Assert.Equal("@epitech", full.GetProperty("company").GetString());
        Assert.NotNull(full.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task The_repos_widget_returns_the_requested_number_in_the_requested_order()
    {
        var login = await LinkAsync();

        var data = await DataAsync(Repos, "count=3&sort=updated");

        Assert.Equal(login, data.GetProperty("login").GetString());
        Assert.Equal("updated", data.GetProperty("sort").GetString());

        var repos = data.GetProperty("repos").EnumerateArray().ToList();
        Assert.Equal(3, repos.Count);
        Assert.Equal("repo-1", repos[0].GetProperty("name").GetString());
        Assert.Equal($"{login}/repo-1", repos[0].GetProperty("fullName").GetString());
        Assert.Equal(10, repos[0].GetProperty("stars").GetInt32());
        Assert.Equal("C#", repos[0].GetProperty("language").GetString());
        Assert.False(repos[0].GetProperty("isFork").GetBoolean());
    }

    [Fact]
    public async Task The_repos_widget_defaults_to_five_most_recently_pushed()
    {
        await LinkAsync();

        var data = await DataAsync(Repos);

        Assert.Equal("pushed", data.GetProperty("sort").GetString());
        Assert.Equal(5, data.GetProperty("count").GetInt32());
        Assert.Equal(5, data.GetProperty("repos").GetArrayLength());
    }

    [Fact]
    public async Task The_activity_widget_counts_only_the_commits_inside_the_window()
    {
        var login = await LinkAsync();

        var week = await DataAsync(Activity, "days=7");
        Assert.Equal(login, week.GetProperty("login").GetString());
        Assert.Equal(FakeGitHubHandler.CommitsLastWeek, week.GetProperty("commits").GetInt32());

        // The same feed over a longer window reaches the ten-day-old push as well.
        var month = await DataAsync(Activity, "days=30");
        Assert.Equal(FakeGitHubHandler.CommitsLastMonth, month.GetProperty("commits").GetInt32());
    }

    [Fact]
    public async Task The_activity_widget_reports_one_entry_per_day_including_the_quiet_ones()
    {
        await LinkAsync();

        var data = await DataAsync(Activity, "days=14");

        var perDay = data.GetProperty("perDay").EnumerateArray().ToList();
        Assert.Equal(14, perDay.Count);
        Assert.Equal(
            FakeGitHubHandler.CommitsLastMonth,
            perDay.Sum(d => d.GetProperty("commits").GetInt32()));

        var days = perDay.Select(d => d.GetProperty("date").GetString()).ToList();
        Assert.Equal(days.OrderBy(d => d).ToList(), days);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), days[^1]);
    }

    [Fact]
    public async Task The_activity_widget_summarises_each_event_in_words()
    {
        await LinkAsync();

        var data = await DataAsync(Activity, "days=7&count=4");

        var events = data.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(4, events.Count);

        Assert.Equal("PushEvent", events[0].GetProperty("type").GetString());
        Assert.Equal("pushed 3 commits", events[0].GetProperty("summary").GetString());

        var summaries = events.Select(e => e.GetProperty("summary").GetString()).ToList();
        Assert.Contains("opened a pull request", summaries);
        Assert.Contains("starred the repository", summaries);
    }

    [Theory]
    [InlineData(Profile, "detail=verbose")]
    [InlineData(Repos, "count=0")]
    [InlineData(Repos, "count=99")]
    [InlineData(Repos, "sort=stars")]
    [InlineData(Activity, "days=0")]
    [InlineData(Activity, "days=99")]
    [InlineData(Activity, "count=99")]
    public async Task A_parameter_outside_what_the_widget_declares_is_refused(string widget, string query)
    {
        await LinkAsync();

        var response = await PreviewAsync(widget, query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unlinking_the_account_makes_the_widgets_refuse_again()
    {
        await LinkAsync();
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(Profile)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/subscriptions/github")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(Profile)).StatusCode);
    }

    [Fact]
    public async Task A_token_github_no_longer_accepts_asks_the_user_to_link_again()
    {
        await LinkAsync();
        await StoreTokenAsync("a-token-github-has-revoked");

        var response = await PreviewAsync(Profile);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var detail = (await JsonAsync(response)).GetProperty("detail").GetString();
        Assert.Contains("link it again", detail);
    }

    [Fact]
    public async Task An_exhausted_rate_limit_says_when_it_resets()
    {
        await LinkAsync(FakeGitHubHandler.RateLimitedLogin);

        // The profile itself still answers; only the endpoints behind it are out of budget.
        Assert.Equal(HttpStatusCode.OK, (await PreviewAsync(Profile)).StatusCode);

        var response = await PreviewAsync(Repos);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var detail = (await JsonAsync(response)).GetProperty("detail").GetString();
        Assert.Contains("rate limit", detail);
        Assert.Contains("resets at", detail);
    }

    [Fact]
    public async Task A_github_widget_can_be_placed_on_the_dashboard_and_the_timer_refreshes_it()
    {
        var login = await LinkAsync();

        var response = await _api.PostAsJsonAsync("/widget-instances", new
        {
            widgetKey = Repos,
            config = new { count = 3, sort = "pushed" },
            refreshRateSeconds = 600,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var instance = await JsonAsync(response);
        Assert.Equal(Repos, instance.GetProperty("widgetKey").GetString());
        Assert.Equal("github", instance.GetProperty("serviceKey").GetString());

        // The OAuth token has to survive decryption inside the timer's own scope, which is the one
        // step a preview does not exercise.
        var id = instance.GetProperty("id").GetInt32();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        JsonElement result;

        while (true)
        {
            result = await JsonAsync(await _api.GetAsync($"/widget-instances/{id}/data"));
            if (result.GetProperty("status").GetString() != "pending") break;

            Assert.True(DateTime.UtcNow < deadline,
                $"Instance {id} never left 'pending'.");
            await Task.Delay(200);
        }

        Assert.Equal("ok", result.GetProperty("status").GetString());
        Assert.Equal(login, result.GetProperty("data").GetProperty("login").GetString());
        Assert.Equal(3, result.GetProperty("data").GetProperty("repos").GetArrayLength());
    }
}
