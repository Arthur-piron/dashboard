using Microsoft.IdentityModel.JsonWebTokens;

namespace DashboardBackEnd.Auth.Oidc;

// Key is the route segment and the options name; LoginProvider is stored in AspNetUserLogins,
// so both are constants rather than anything derived from a class name.
public sealed record OidcProvider(
    string Key,
    string DisplayName,
    string LoginProvider,
    string ConfigSection,
    Func<OidcProviderOptions, string> MetadataAddress,
    IReadOnlyList<string> AlternateIssuers,
    Func<JsonWebToken, string?> Email,
    Func<JsonWebToken, bool> EmailIsVerified);

public static class OidcProviders
{
    public static readonly OidcProvider Google = new(
        Key: "google",
        DisplayName: "Google",
        LoginProvider: "Google",
        ConfigSection: "Google",
        MetadataAddress: _ => "https://accounts.google.com/.well-known/openid-configuration",
        // Google documents both spellings for the iss of its ID tokens.
        AlternateIssuers: ["accounts.google.com"],
        Email: token => Claim(token, "email"),
        EmailIsVerified: token => Flag(token, "email_verified"));

    public static readonly OidcProvider Microsoft = new(
        Key: "microsoft",
        DisplayName: "Microsoft",
        LoginProvider: "Microsoft",
        ConfigSection: "Microsoft",
        MetadataAddress: options =>
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(options.Tenant)}/v2.0/.well-known/openid-configuration",
        AlternateIssuers: [],
        Email: token => Claim(token, "email")
            ?? (Claim(token, "preferred_username") is { } upn && upn.Contains('@') ? upn : null),
        // An Entra tenant admin can set any address on an account (the "nOAuth" attack),
        // so a Microsoft email is never proof that the user owns the mailbox.
        EmailIsVerified: _ => false);

    public static readonly IReadOnlyList<OidcProvider> All = [Google, Microsoft];

    public static OidcProvider? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private static string? Claim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<string>(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool Flag(JsonWebToken token, string name) =>
        (token.TryGetPayloadValue<bool>(name, out var flag) && flag)
        || string.Equals(Claim(token, name), "true", StringComparison.OrdinalIgnoreCase);
}
