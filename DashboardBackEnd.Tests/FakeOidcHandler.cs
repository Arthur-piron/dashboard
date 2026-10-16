using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DashboardBackEnd.Tests;

// What the test wants the provider to assert about the user. The real provider learns the nonce and
// the PKCE challenge from the authorize URL; here the test copies them out of it into the code.
public record FakeIdentity(
    string Sub,
    string? Email,
    string Nonce,
    string Challenge,
    bool EmailVerified = true,
    string? Tid = null,
    string? PreferredUsername = null,
    string? Audience = null,
    string? Issuer = null,
    bool SignWithForeignKey = false);

// Stands in for Google's and Microsoft's discovery documents, signing keys and token endpoints,
// so the OIDC flow runs offline. The authorization code is the FakeIdentity itself, encoded.
public class FakeOidcHandler : HttpMessageHandler
{
    public const string KeyId = "test-key";

    public const string GoogleIssuer = "https://accounts.google.com";
    public const string MicrosoftTemplatedIssuer = "https://login.microsoftonline.com/{tenantid}/v2.0";

    // A consumer-style tenant id: what personal Microsoft accounts carry in tid.
    public const string DefaultTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    private static readonly RSA SigningRsa = RSA.Create(2048);
    private static readonly RSA ForeignRsa = RSA.Create(2048);

    public static string CodeFor(FakeIdentity identity) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(identity));

    public static string ChallengeFor(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;

        return (uri.Host, uri.AbsolutePath) switch
        {
            ("accounts.google.com", "/.well-known/openid-configuration") => Json(HttpStatusCode.OK, GoogleMetadata),
            ("www.googleapis.com", "/oauth2/v3/certs") => Json(HttpStatusCode.OK, Jwks()),
            ("oauth2.googleapis.com", "/token") => await TokenAsync(request, google: true, ct),

            ("login.microsoftonline.com", "/common/v2.0/.well-known/openid-configuration") =>
                Json(HttpStatusCode.OK, MicrosoftMetadata),
            ("login.microsoftonline.com", "/common/discovery/v2.0/keys") => Json(HttpStatusCode.OK, Jwks()),
            ("login.microsoftonline.com", "/common/oauth2/v2.0/token") => await TokenAsync(request, google: false, ct),

            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static readonly string GoogleMetadata = JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["issuer"] = GoogleIssuer,
        ["authorization_endpoint"] = "https://accounts.google.com/o/oauth2/v2/auth",
        ["token_endpoint"] = "https://oauth2.googleapis.com/token",
        ["jwks_uri"] = "https://www.googleapis.com/oauth2/v3/certs",
        ["response_types_supported"] = new[] { "code" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    });

    private static readonly string MicrosoftMetadata = JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["issuer"] = MicrosoftTemplatedIssuer,
        ["authorization_endpoint"] = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        ["token_endpoint"] = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        ["jwks_uri"] = "https://login.microsoftonline.com/common/discovery/v2.0/keys",
        ["response_types_supported"] = new[] { "code" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
    });

    private static string Jwks()
    {
        var key = SigningRsa.ExportParameters(includePrivateParameters: false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA", use = "sig", alg = "RS256", kid = KeyId,
                    n = WebEncoders.Base64UrlEncode(key.Modulus!),
                    e = WebEncoders.Base64UrlEncode(key.Exponent!),
                },
            },
        });
    }

    private static async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request, bool google, CancellationToken ct)
    {
        var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));

        var (clientId, secret) = google
            ? (ApiFactory.GoogleClientId, ApiFactory.GoogleClientSecret)
            : (ApiFactory.MicrosoftClientId, ApiFactory.MicrosoftClientSecret);

        if (form["grant_type"] != "authorization_code" || form["client_id"] != clientId
            || form["client_secret"] != secret || string.IsNullOrEmpty(form["redirect_uri"]))
        {
            return Json(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        }

        FakeIdentity? identity;
        try
        {
            identity = JsonSerializer.Deserialize<FakeIdentity>(WebEncoders.Base64UrlDecode(form["code"] ?? ""));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            identity = null;
        }

        if (identity is null || ChallengeFor(form["code_verifier"] ?? "") != identity.Challenge)
        {
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        }

        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = "fake-access-token",
            token_type = "Bearer",
            expires_in = 3600,
            id_token = IdToken(identity, google, clientId),
        }));
    }

    private static string IdToken(FakeIdentity identity, bool google, string clientId)
    {
        var tenant = identity.Tid ?? DefaultTenant;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = identity.Sub,
            ["nonce"] = identity.Nonce,
            ["name"] = "Test User",
        };
        if (identity.Email is not null) claims["email"] = identity.Email;
        if (google) claims["email_verified"] = identity.EmailVerified;
        if (!google) claims["tid"] = tenant;
        if (identity.PreferredUsername is not null) claims["preferred_username"] = identity.PreferredUsername;

        var rsa = identity.SignWithForeignKey ? ForeignRsa : SigningRsa;
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = identity.Issuer ?? (google ? GoogleIssuer : MicrosoftTemplatedIssuer.Replace("{tenantid}", tenant)),
            Audience = identity.Audience ?? clientId,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
