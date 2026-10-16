using System.Security.Claims;
using System.Security.Cryptography;
using DashboardBackEnd.Models;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Services.GitHub;
using DashboardBackEnd.Widgets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DashboardBackEnd.Subscriptions;

// OAuth 2.0 authorization code flow, by hand: /authorize hands the browser a GitHub URL,
// GitHub sends it back to /callback, and the code is exchanged for a token that is stored
// encrypted as the user's subscription to the 'github' service.
public static class GitHubOAuthEndpoints
{
    public const string LoginProvider = "GitHub";

    private const string StatePurpose = "DashboardBackEnd.GitHubOAuthState.v1";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapGitHubOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/services/github/oauth").WithTags("OAuth");

        group.MapGet("/authorize", Authorize).RequireAuthorization();

        // Anonymous on purpose: GitHub redirects the browser here, and the browser carries no JWT.
        // The signed, time-limited state is what ties the callback back to the user who started it.
        group.MapGet("/callback", Callback);

        return app;
    }

    private static IResult Authorize(
        ClaimsPrincipal principal,
        HttpContext http,
        IOptions<GitHubOptions> options,
        IDataProtectionProvider protection)
    {
        var github = options.Value;
        if (!github.IsConfigured) return NotConfigured();

        var state = StateProtector(protection).Protect(principal.UserId(), StateLifetime);

        return Results.Ok(new GitHubAuthorizeResponse(
            GitHubClient.AuthorizeUrl(github.ClientId!, CallbackUrl(github, http), state)));
    }

    private static async Task<IResult> Callback(
        HttpContext http,
        IOptions<GitHubOptions> options,
        IConfiguration config,
        IDataProtectionProvider protection,
        GitHubClient github,
        SubscriptionService subscriptions,
        UserManager<AppUser> users,
        ILogger<GitHubOptions> logger,
        CancellationToken ct,
        string? code = null,
        string? state = null,
        string? error = null)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) return NotConfigured();

        // Every failure past this point becomes a redirect: a problem document would land in the
        // browser's address bar, where the user cannot act on it.
        if (!string.IsNullOrEmpty(error)) return Back(config, "denied");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Back(config, "invalid_request");

        string userId;
        try
        {
            userId = StateProtector(protection).Unprotect(state);
        }
        catch (CryptographicException)
        {
            return Back(config, "invalid_state");
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null) return Back(config, "unknown_user");

        var token = await github.ExchangeCodeAsync(
            settings.ClientId!, settings.ClientSecret!, code, CallbackUrl(settings, http), ct);
        if (token is null)
        {
            logger.LogWarning("GitHub refused the code exchange for user {UserId}.", userId);
            return Back(config, "exchange_failed");
        }

        await subscriptions.SubscribeAsync(
            userId, GitHubService.ServiceKey, new ServiceCredentials(AccessToken: token.Value), ct: ct);

        var account = await github.GetAccountAsync(token.Value, ct);
        if (account is not null && await users.FindByLoginAsync(LoginProvider, account.Id) is null)
        {
            // Records the external identity too, so "log in with GitHub" can find this user later.
            await users.AddLoginAsync(user, new UserLoginInfo(LoginProvider, account.Id, account.Login));
        }

        return Back(config, failure: null, account?.Login);
    }

    private static IResult NotConfigured() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "GitHub is not configured",
        detail: "Set GitHub:ClientId and GitHub:ClientSecret to enable linking a GitHub account.");

    private static IResult Back(IConfiguration config, string? failure, string? login = null)
    {
        var frontend = config["Frontend:Url"]?.TrimEnd('/') ?? "";

        var query = failure is null
            ? "?github=linked" + (login is null ? "" : $"&account={Uri.EscapeDataString(login)}")
            : $"?github=failed&reason={Uri.EscapeDataString(failure)}";

        return Results.Redirect($"{frontend}/services{query}");
    }

    private static string CallbackUrl(GitHubOptions options, HttpContext http) =>
        options.CallbackUrl is { Length: > 0 } url
            ? url
            : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/services/github/oauth/callback";

    private static ITimeLimitedDataProtector StateProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();
}
