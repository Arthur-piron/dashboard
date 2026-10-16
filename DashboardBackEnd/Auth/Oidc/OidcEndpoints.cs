using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DashboardBackEnd.Auth.Oidc;

public record OidcProviderDto(string Key, string Name);
public record OidcAuthorizeResponse(string Url);

// "Sign in with Google / Microsoft": the OpenID Connect authorization code flow with PKCE, by hand.
// /authorize hands the SPA a provider URL, the provider sends the browser back to /callback, and
// the callback answers with a redirect carrying our own JWT.
public static class OidcEndpoints
{
    private const string StatePurpose = "DashboardBackEnd.OidcState.v1";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private record OidcState(string Provider, string Nonce, string Verifier, string Binding);

    public static IEndpointRouteBuilder MapOidcEndpoints(this IEndpointRouteBuilder app)
    {
        // All anonymous: the user is signing in, so there is no JWT yet.
        var group = app.MapGroup("/auth/oidc").WithTags("Auth");

        group.MapGet("/providers", Providers);
        group.MapGet("/{provider}/authorize", Authorize);
        group.MapGet("/{provider}/callback", Callback);

        return app;
    }

    private static IReadOnlyList<OidcProviderDto> Providers(OidcClient oidc) =>
        OidcProviders.All
            .Where(p => oidc.Options(p).IsConfigured)
            .Select(p => new OidcProviderDto(p.Key, p.DisplayName))
            .ToList();

    private static async Task<IResult> Authorize(
        string provider,
        HttpContext http,
        OidcClient oidc,
        IDataProtectionProvider protection,
        ILogger<OidcClient> logger,
        CancellationToken ct)
    {
        var known = OidcProviders.Find(provider);
        if (known is null) return Results.NotFound();
        if (!oidc.Options(known).IsConfigured) return NotConfigured(known);

        var state = new OidcState(known.Key, RandomToken(), RandomToken(), RandomToken());

        // The binding ties the callback to the browser that started the flow. Without it, an attacker
        // could send someone a callback link carrying the attacker's own code and sign them in as
        // the attacker (login CSRF). Lax still reaches the top-level redirect back from the provider.
        http.Response.Cookies.Append(BindingCookie(known), state.Binding, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Path = "/",
            MaxAge = StateLifetime,
            IsEssential = true,
        });

        try
        {
            var url = await oidc.AuthorizeUrlAsync(
                known,
                RedirectUri(known, oidc, http),
                StateProtector(protection).Protect(JsonSerializer.Serialize(state), StateLifetime),
                state.Nonce,
                CodeChallenge(state.Verifier),
                ct);

            return Results.Ok(new OidcAuthorizeResponse(url));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "Could not read the {Provider} OpenID configuration.", known.DisplayName);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: $"{known.DisplayName} is unreachable",
                detail: $"Signing in with {known.DisplayName} is not possible right now. Try again in a moment.");
        }
    }

    private static async Task<IResult> Callback(
        string provider,
        HttpContext http,
        OidcClient oidc,
        OidcSignIn signIn,
        UserManager<AppUser> users,
        TokenService tokens,
        IDataProtectionProvider protection,
        IConfiguration config,
        ILogger<OidcClient> logger,
        CancellationToken ct,
        string? code = null,
        string? state = null,
        string? error = null,
        [FromQuery(Name = "error_description")] string? errorDescription = null)
    {
        var known = OidcProviders.Find(provider);
        if (known is null) return Results.NotFound();
        if (!oidc.Options(known).IsConfigured) return NotConfigured(known);

        // Every failure past this point becomes a redirect: a problem document would land in the
        // browser's address bar, where the user cannot act on it.
        var binding = http.Request.Cookies[BindingCookie(known)];
        http.Response.Cookies.Delete(BindingCookie(known), new CookieOptions { Path = "/" });

        if (!string.IsNullOrEmpty(error))
        {
            if (error == "access_denied") return Failed(config, known, "denied");

            // Anything else is the provider refusing our request, usually its app registration.
            logger.LogWarning("{Provider} refused the sign-in request: {Error} {Description}",
                known.DisplayName, error, errorDescription);
            return Failed(config, known, "provider_error");
        }
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)) return Failed(config, known, "invalid_request");

        var sealedState = Unseal(protection, state);
        if (sealedState is null || sealedState.Provider != known.Key
            || string.IsNullOrEmpty(binding) || !FixedTimeEquals(binding, sealedState.Binding))
        {
            return Failed(config, known, "invalid_state");
        }

        JsonWebToken? idToken;
        try
        {
            var raw = await oidc.ExchangeCodeAsync(known, code, RedirectUri(known, oidc, http), sealedState.Verifier, ct);
            if (raw is null)
            {
                logger.LogWarning("{Provider} refused the code exchange.", known.DisplayName);
                return Failed(config, known, "exchange_failed");
            }

            idToken = await oidc.ValidateIdTokenAsync(known, raw, sealedState.Nonce, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException
            or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not complete the {Provider} sign-in.", known.DisplayName);
            return Failed(config, known, "unavailable");
        }

        if (idToken is null)
        {
            logger.LogWarning("Rejected an id_token from {Provider}.", known.DisplayName);
            return Failed(config, known, "invalid_token");
        }

        var result = await signIn.ResolveAsync(known, idToken, ct);
        if (result.User is null) return Failed(config, known, result.Failure!);

        var roles = await users.GetRolesAsync(result.User);
        var (token, expiresAt) = tokens.CreateToken(result.User, roles);

        // A fragment never reaches a server, so the JWT stays out of every access log on the way.
        var fragment = $"token={Uri.EscapeDataString(token)}&expiresAt={Uri.EscapeDataString(expiresAt.ToString("O"))}";
        return Results.Redirect($"{Frontend(config)}/auth/callback#{fragment}");
    }

    private static IResult NotConfigured(OidcProvider provider) => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: $"{provider.DisplayName} is not configured",
        detail: $"Set Oidc:{provider.ConfigSection}:ClientId and ClientSecret to enable signing in with {provider.DisplayName}.");

    private static IResult Failed(IConfiguration config, OidcProvider provider, string reason) =>
        Results.Redirect(
            $"{Frontend(config)}/login?oidc=failed&provider={provider.Key}&reason={Uri.EscapeDataString(reason)}");

    private static string Frontend(IConfiguration config) => config["Frontend:Url"]?.TrimEnd('/') ?? "";

    private static string RedirectUri(OidcProvider provider, OidcClient oidc, HttpContext http) =>
        oidc.Options(provider).CallbackUrl is { Length: > 0 } url
            ? url
            : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/auth/oidc/{provider.Key}/callback";

    private static string BindingCookie(OidcProvider provider) => $"oidc-binding-{provider.Key}";

    private static OidcState? Unseal(IDataProtectionProvider protection, string state)
    {
        try
        {
            return JsonSerializer.Deserialize<OidcState>(StateProtector(protection).Unprotect(state));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static ITimeLimitedDataProtector StateProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();

    private static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CodeChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
