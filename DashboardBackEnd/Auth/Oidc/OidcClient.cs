using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DashboardBackEnd.Auth.Oidc;

// Speaks OpenID Connect to the providers: discovery, the authorize URL, the code exchange and
// the validation of the id_token. Deciding which Dashboard user that token means is OidcSignIn's job.
public class OidcClient
{
    public const string HttpClientName = "oidc";

    private const string Scopes = "openid email profile";

    private readonly IHttpClientFactory _http;
    private readonly IOptionsMonitor<OidcProviderOptions> _options;
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _metadata = new();

    public OidcClient(IHttpClientFactory http, IOptionsMonitor<OidcProviderOptions> options)
    {
        _http = http;
        _options = options;
    }

    public OidcProviderOptions Options(OidcProvider provider) => _options.Get(provider.Key);

    // Cached and refreshed by the ConfigurationManager, so signing key rotation is picked up.
    private ConfigurationManager<OpenIdConnectConfiguration> Metadata(OidcProvider provider) =>
        _metadata.GetOrAdd(provider.Key, _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            provider.MetadataAddress(Options(provider)),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_http.CreateClient(HttpClientName)) { RequireHttps = true }));

    public async Task<string> AuthorizeUrlAsync(
        OidcProvider provider, string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct)
    {
        var metadata = await Metadata(provider).GetConfigurationAsync(ct);

        return QueryHelpers.AddQueryString(metadata.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = Options(provider).ClientId,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account",
        });
    }

    // Null when the provider refuses the code: a non-2xx, or an "error" field in the body.
    public async Task<string?> ExchangeCodeAsync(
        OidcProvider provider, string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var metadata = await Metadata(provider).GetConfigurationAsync(ct);
        var options = Options(provider);

        var client = _http.CreateClient(HttpClientName);
        using var response = await client.PostAsync(metadata.TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["code_verifier"] = codeVerifier,
            }), ct);

        if (!response.IsSuccessStatusCode) return null;

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (body.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.TryGetProperty("error", out _))
        {
            return null;
        }

        return body.RootElement.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null;
    }

    // Null unless the signature, issuer, audience, lifetime and nonce all check out.
    public async Task<JsonWebToken?> ValidateIdTokenAsync(
        OidcProvider provider, string idToken, string expectedNonce, CancellationToken ct)
    {
        var manager = Metadata(provider);
        var result = await ValidateAsync(provider, idToken, await manager.GetConfigurationAsync(ct));

        // A key we have never seen may be a rotation: refetch once before giving up.
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            manager.RequestRefresh();
            result = await ValidateAsync(provider, idToken, await manager.GetConfigurationAsync(ct));
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) return null;

        return token.TryGetPayloadValue<string>("nonce", out var nonce) && nonce == expectedNonce ? token : null;
    }

    private Task<TokenValidationResult> ValidateAsync(
        OidcProvider provider, string idToken, OpenIdConnectConfiguration metadata) =>
        new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidAudience = Options(provider).ClientId,
            IssuerSigningKeys = metadata.SigningKeys,
            IssuerValidator = (issuer, token, _) => IsExpectedIssuer(provider, metadata, issuer, token)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Unexpected issuer '{issuer}'."),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
        });

    // Microsoft's multi-tenant metadata publishes "https://login.microsoftonline.com/{tenantid}/v2.0":
    // the real issuer names the tenant of the account, carried in the token's own tid claim.
    private static bool IsExpectedIssuer(
        OidcProvider provider, OpenIdConnectConfiguration metadata, string issuer, SecurityToken token)
    {
        var expected = metadata.Issuer;
        if (expected.Contains("{tenantid}"))
        {
            if (token is not JsonWebToken jwt || !jwt.TryGetPayloadValue<string>("tid", out var tenant)
                || string.IsNullOrEmpty(tenant))
            {
                return false;
            }

            expected = expected.Replace("{tenantid}", tenant);
        }

        return issuer == expected || provider.AlternateIssuers.Contains(issuer);
    }
}
