using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using DashboardBackEnd.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

public class GitHubOAuthTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Authorize = GitHubLink.Authorize;
    private const string Callback = GitHubLink.Callback;

    private readonly ApiFactory _factory;
    private readonly HttpClient _api;
    private readonly HttpClient _browser;
    private readonly MailpitClient _mailpit = new();

    public GitHubOAuthTests(ApiFactory factory)
    {
        _factory = factory;
        _api = factory.CreateClient();

        // GitHub redirects a browser that carries no JWT and must not follow our redirect back.
        _browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private string FrontendUrl => _factory.Services
        .GetRequiredService<IConfiguration>()["Frontend:Url"]!
        .TrimEnd('/');

    private Task<string> StateAsync() => GitHubLink.StateAsync(_api);

    private Task<HttpResponseMessage> CallbackAsync(string query) =>
        GitHubLink.CallbackAsync(_browser, query);

    private static string Redirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    private async Task<bool> IsSubscribedAsync()
    {
        var services = await JsonAsync(await _api.GetAsync("/services"));
        return services.EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == "github")
            .GetProperty("subscribed").GetBoolean();
    }

    [Fact]
    public async Task GitHub_is_linked_through_oauth_and_asks_for_nothing_to_paste()
    {
        var services = await JsonAsync(await _api.GetAsync("/services"));

        var github = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "github");
        Assert.Equal("oauth", github.GetProperty("auth").GetString());
        Assert.Equal(JsonValueKind.Null, github.GetProperty("credentialPrompt").ValueKind);
        Assert.False(github.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Starting_the_flow_requires_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(Authorize);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_hands_back_githubs_url_with_the_registered_callback_and_a_state()
    {
        var url = new Uri((await JsonAsync(await _api.GetAsync(Authorize))).GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://github.com/login/oauth/authorize", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(ApiFactory.GitHubClientId, query["client_id"]);
        Assert.Equal(ApiFactory.GitHubCallbackUrl, query["redirect_uri"]);
        Assert.Equal("read:user", query["scope"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
    }

    [Fact]
    public async Task The_state_is_opaque_and_carries_no_readable_user_id()
    {
        var me = await JsonAsync(await _api.GetAsync("/auth/me"));
        var userId = me.GetProperty("id").GetString()!;

        var state = await StateAsync();

        Assert.DoesNotContain(userId, state);
    }

    [Fact]
    public async Task A_completed_flow_subscribes_the_user_and_sends_the_browser_back_to_the_front_end()
    {
        Assert.False(await IsSubscribedAsync());

        var login = GitHubLink.NewLogin();
        var response = await CallbackAsync($"code={FakeGitHubHandler.CodeFor(login)}&state={await StateAsync()}");

        var location = Redirect(response);
        // Read from configuration, not hardcoded: Frontend:Url is http://localhost:5173 for
        // `dotnet test` on the host and http://localhost:8081 for `docker compose run tests`.
        Assert.StartsWith($"{FrontendUrl}/services", location);
        Assert.Contains("github=linked", location);
        Assert.Contains($"account={login}", location);

        Assert.True(await IsSubscribedAsync());
    }

    [Fact]
    public async Task The_access_token_is_stored_encrypted_and_never_comes_back_out()
    {
        var login = GitHubLink.NewLogin();
        await CallbackAsync($"code={FakeGitHubHandler.CodeFor(login)}&state={await StateAsync()}");

        var token = FakeGitHubHandler.TokenFor(login);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ServiceSubscriptions
            .Where(s => s.ServiceKey == "github")
            .Select(s => s.ProtectedCredentials)
            .ToListAsync();

        Assert.NotEmpty(stored);
        Assert.All(stored, value => Assert.DoesNotContain(token, value));

        foreach (var url in new[] { "/subscriptions", "/services", "/widgets" })
        {
            Assert.DoesNotContain(token, await (await _api.GetAsync(url)).Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_completed_flow_also_records_the_external_login()
    {
        var login = GitHubLink.NewLogin();
        await CallbackAsync($"code={FakeGitHubHandler.CodeFor(login)}&state={await StateAsync()}");

        var me = await JsonAsync(await _api.GetAsync("/auth/me"));
        var userId = me.GetProperty("id").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.UserLogins
            .SingleOrDefaultAsync(l => l.UserId == userId && l.LoginProvider == "GitHub");

        Assert.NotNull(stored);
        Assert.Equal(FakeGitHubHandler.AccountIdFor(login), stored.ProviderKey);
    }

    [Fact]
    public async Task Unlinking_removes_the_subscription()
    {
        var login = GitHubLink.NewLogin();
        await CallbackAsync($"code={FakeGitHubHandler.CodeFor(login)}&state={await StateAsync()}");
        Assert.True(await IsSubscribedAsync());

        var response = await _api.DeleteAsync("/subscriptions/github");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await IsSubscribedAsync());
    }

    [Theory]
    [InlineData("state=forged-state&code=whatever", "invalid_state")]
    [InlineData("code=only-a-code", "invalid_request")]
    [InlineData("state=only-a-state", "invalid_request")]
    [InlineData("error=access_denied&state=anything", "denied")]
    public async Task A_failed_callback_sends_the_browser_back_with_a_reason(string query, string reason)
    {
        var location = Redirect(await CallbackAsync(query));

        Assert.Contains("github=failed", location);
        Assert.Contains($"reason={reason}", location);
        Assert.False(await IsSubscribedAsync());
    }

    [Fact]
    public async Task A_code_github_refuses_stores_nothing()
    {
        var location = Redirect(await CallbackAsync($"code=expired-code&state={await StateAsync()}"));

        Assert.Contains("reason=exchange_failed", location);
        Assert.False(await IsSubscribedAsync());
    }

    [Fact]
    public async Task The_flow_is_unavailable_while_no_oauth_app_is_configured()
    {
        using var unconfigured = new ApiFactoryWithoutGitHub();
        using var client = unconfigured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await TestUser.CreateAndLoginAsync(client, _mailpit));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(Authorize)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"{Callback}?code=a&state=b")).StatusCode);
    }

    // A clone with no OAuth app registered: the API must still boot and say so politely.
    private class ApiFactoryWithoutGitHub : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitHub:ClientId"] = "",
                    ["GitHub:ClientSecret"] = "",
                }));
        }
    }
}
