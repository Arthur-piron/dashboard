using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using DashboardBackEnd.Auth;
using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

public class OidcLoginTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _browser;
    private readonly MailpitClient _mailpit = new();

    public OidcLoginTests(ApiFactory factory)
    {
        _factory = factory;
        _browser = OidcLogin.Browser(factory);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<JsonElement> MeAsync(string token, WebApplicationFactory<Program>? factory = null)
    {
        using var api = (factory ?? _factory).CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await api.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private async Task<int> LoginCountAsync(string provider, string sub)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserLogins.CountAsync(l => l.LoginProvider == provider && l.ProviderKey == sub);
    }

    private WebApplicationFactory<Program> With(Dictionary<string, string?> settings) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));

    [Fact]
    public async Task Providers_lists_the_configured_ones_without_authentication()
    {
        var providers = await JsonAsync(await _factory.CreateClient().GetAsync("/auth/oidc/providers"));

        Assert.Equal(
            ["google", "microsoft"],
            providers.EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToArray());
        Assert.Equal("Google", providers[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_unconfigured_provider_is_hidden_and_answers_503()
    {
        await using var factory = With(new() { ["Oidc:Google:ClientId"] = "" });
        using var api = factory.CreateClient();

        var providers = await JsonAsync(await api.GetAsync("/auth/oidc/providers"));
        Assert.Equal(["microsoft"], providers.EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToArray());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.GetAsync("/auth/oidc/google/authorize")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_provider_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync("/auth/oidc/facebook/authorize")).StatusCode);
    }

    [Fact]
    public async Task Authorize_builds_a_code_flow_url_with_pkce_and_sets_the_binding_cookie()
    {
        var response = await _browser.GetAsync("/auth/oidc/google/authorize");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("oidc-binding-google=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var url = new Uri((await JsonAsync(response)).GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(ApiFactory.GoogleClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Contains("openid", query["scope"]!.Split(' '));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.EndsWith("/auth/oidc/google/callback", query["redirect_uri"]);
    }

    [Fact]
    public async Task A_first_google_sign_in_creates_an_account_and_returns_a_working_token()
    {
        var sub = OidcLogin.NewSub();
        var email = OidcLogin.NewEmail();

        var token = await OidcLogin.SignInAsync(_browser, "google", a => new FakeIdentity(sub, email, a.Nonce, a.Challenge));

        var me = await MeAsync(token);
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("username").GetString()));
        Assert.Equal(1, await LoginCountAsync("Google", sub));
    }

    [Fact]
    public async Task Signing_in_again_finds_the_same_account()
    {
        var sub = OidcLogin.NewSub();
        var email = OidcLogin.NewEmail();

        var first = await OidcLogin.SignInAsync(_browser, "google", a => new FakeIdentity(sub, email, a.Nonce, a.Challenge));
        var second = await OidcLogin.SignInAsync(_browser, "google", a => new FakeIdentity(sub, email, a.Nonce, a.Challenge));

        Assert.Equal((await MeAsync(first)).GetProperty("id").GetString(), (await MeAsync(second)).GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_verified_google_email_links_to_the_confirmed_account_that_owns_it()
    {
        var email = OidcLogin.NewEmail();
        var password = await TestUser.CreateAndLoginAsync(_factory.CreateClient(), _mailpit, email);
        var sub = OidcLogin.NewSub();

        var token = await OidcLogin.SignInAsync(_browser, "google", a => new FakeIdentity(sub, email, a.Nonce, a.Challenge));

        Assert.Equal((await MeAsync(password)).GetProperty("id").GetString(), (await MeAsync(token)).GetProperty("id").GetString());
        Assert.Equal(1, await LoginCountAsync("Google", sub));
    }

    [Fact]
    public async Task An_unconfirmed_account_with_the_same_email_is_not_linked()
    {
        var email = OidcLogin.NewEmail();
        await _factory.CreateClient().PostAsJsonAsync("/auth/register",
            new { email, username = $"u{Guid.NewGuid():N}"[..12], password = TestUser.Password });
        var sub = OidcLogin.NewSub();

        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");
        var response = await OidcLogin.CompleteAsync(_browser, "google", authorization,
            new FakeIdentity(sub, email, authorization.Nonce, authorization.Challenge));

        Assert.Equal("email_not_confirmed", OidcLogin.FailureReason(response));
        Assert.Equal(0, await LoginCountAsync("Google", sub));
    }

    [Fact]
    public async Task An_unverified_google_email_never_takes_over_an_existing_account()
    {
        var email = OidcLogin.NewEmail();
        await TestUser.CreateAndLoginAsync(_factory.CreateClient(), _mailpit, email);

        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");
        var response = await OidcLogin.CompleteAsync(_browser, "google", authorization,
            new FakeIdentity(OidcLogin.NewSub(), email, authorization.Nonce, authorization.Challenge, EmailVerified: false));

        Assert.Equal("email_in_use", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_microsoft_sign_in_creates_an_account_through_the_templated_issuer()
    {
        var sub = OidcLogin.NewSub();
        var email = OidcLogin.NewEmail();

        var token = await OidcLogin.SignInAsync(_browser, "microsoft",
            a => new FakeIdentity(sub, email, a.Nonce, a.Challenge, Tid: Guid.NewGuid().ToString()));

        Assert.Equal(email, (await MeAsync(token)).GetProperty("email").GetString());
        Assert.Equal(1, await LoginCountAsync("Microsoft", sub));
    }

    [Fact]
    public async Task A_microsoft_account_falls_back_to_preferred_username_for_its_email()
    {
        var upn = OidcLogin.NewEmail();

        var token = await OidcLogin.SignInAsync(_browser, "microsoft",
            a => new FakeIdentity(OidcLogin.NewSub(), null, a.Nonce, a.Challenge, PreferredUsername: upn));

        Assert.Equal(upn, (await MeAsync(token)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task A_microsoft_email_never_links_to_an_existing_account()
    {
        var email = OidcLogin.NewEmail();
        await TestUser.CreateAndLoginAsync(_factory.CreateClient(), _mailpit, email);
        var sub = OidcLogin.NewSub();

        var authorization = await OidcLogin.AuthorizeAsync(_browser, "microsoft");
        var response = await OidcLogin.CompleteAsync(_browser, "microsoft", authorization,
            new FakeIdentity(sub, email, authorization.Nonce, authorization.Challenge));

        Assert.Equal("email_in_use", OidcLogin.FailureReason(response));
        Assert.Equal(0, await LoginCountAsync("Microsoft", sub));
    }

    [Fact]
    public async Task A_token_without_any_email_is_refused()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "microsoft");
        var response = await OidcLogin.CompleteAsync(_browser, "microsoft", authorization,
            new FakeIdentity(OidcLogin.NewSub(), null, authorization.Nonce, authorization.Challenge));

        Assert.Equal("no_email", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_callback_from_another_browser_is_refused()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");
        using var otherBrowser = OidcLogin.Browser(_factory);

        var response = await OidcLogin.CompleteAsync(otherBrowser, "google", authorization,
            new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), authorization.Nonce, authorization.Challenge));

        Assert.Equal("invalid_state", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_state_issued_for_one_provider_is_refused_by_another()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");
        await OidcLogin.AuthorizeAsync(_browser, "microsoft");

        var response = await OidcLogin.CompleteAsync(_browser, "microsoft", authorization,
            new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), authorization.Nonce, authorization.Challenge));

        Assert.Equal("invalid_state", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_tampered_state_is_refused()
    {
        await OidcLogin.AuthorizeAsync(_browser, "google");

        var response = await OidcLogin.CallbackAsync(_browser, "google", "code=anything&state=not-a-sealed-state");

        Assert.Equal("invalid_state", OidcLogin.FailureReason(response));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    public async Task An_id_token_that_fails_validation_is_refused(string flaw)
    {
        var a = await OidcLogin.AuthorizeAsync(_browser, "google");
        var identity = new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), a.Nonce, a.Challenge);
        identity = flaw switch
        {
            "nonce" => identity with { Nonce = "another-nonce" },
            "signature" => identity with { SignWithForeignKey = true },
            "audience" => identity with { Audience = "someone-elses-client" },
            _ => identity with { Issuer = "https://evil.example.com" },
        };

        var response = await OidcLogin.CompleteAsync(_browser, "google", a, identity);

        Assert.Equal("invalid_token", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_code_redeemed_without_the_right_pkce_verifier_is_refused()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");

        var response = await OidcLogin.CompleteAsync(_browser, "google", authorization,
            new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), authorization.Nonce, "a-challenge-for-another-verifier"));

        Assert.Equal("exchange_failed", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task Refusing_consent_sends_the_browser_back_with_denied()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");

        var response = await OidcLogin.CallbackAsync(_browser, "google",
            $"error=access_denied&state={Uri.EscapeDataString(authorization.State)}");

        Assert.Equal("denied", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task A_provider_refusing_the_request_is_not_reported_as_a_cancellation()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "microsoft");

        var response = await OidcLogin.CallbackAsync(_browser, "microsoft",
            "error=invalid_request&error_description=AADSTS50194%3a+not+multi-tenant"
            + $"&state={Uri.EscapeDataString(authorization.State)}");

        Assert.Equal("provider_error", OidcLogin.FailureReason(response));
    }

    [Fact]
    public async Task The_callback_clears_the_binding_cookie()
    {
        var authorization = await OidcLogin.AuthorizeAsync(_browser, "google");
        var response = await OidcLogin.CompleteAsync(_browser, "google", authorization,
            new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), authorization.Nonce, authorization.Challenge));

        // Replaying the same callback in the same browser no longer has a binding to match.
        var replay = await OidcLogin.CompleteAsync(_browser, "google", authorization,
            new FakeIdentity(OidcLogin.NewSub(), OidcLogin.NewEmail(), authorization.Nonce, authorization.Challenge));

        Assert.Contains("/auth/callback#", response.Headers.Location!.ToString());
        Assert.Equal("invalid_state", OidcLogin.FailureReason(replay));
    }

    [Fact]
    public async Task A_verified_google_email_matching_the_configured_admin_is_promoted()
    {
        var email = OidcLogin.NewEmail();
        await using var factory = With(new() { ["Admin:Email"] = email });
        using var browser = OidcLogin.Browser(factory);

        var token = await OidcLogin.SignInAsync(browser, "google",
            a => new FakeIdentity(OidcLogin.NewSub(), email, a.Nonce, a.Challenge));

        Assert.Contains(AppRoles.Admin, (await MeAsync(token, factory)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task A_microsoft_email_matching_the_configured_admin_is_not_promoted()
    {
        var email = OidcLogin.NewEmail();
        await using var factory = With(new() { ["Admin:Email"] = email });
        using var browser = OidcLogin.Browser(factory);

        var token = await OidcLogin.SignInAsync(browser, "microsoft",
            a => new FakeIdentity(OidcLogin.NewSub(), email, a.Nonce, a.Challenge));

        Assert.Empty((await MeAsync(token, factory)).GetProperty("roles").EnumerateArray());

        // Nor on the next start, where the seed looks the configured address up again.
        await using var restarted = With(new() { ["Admin:Email"] = email });
        using var scope = restarted.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.False(await users.IsInRoleAsync((await users.FindByEmailAsync(email))!, AppRoles.Admin));
    }
}
