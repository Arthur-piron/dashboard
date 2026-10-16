namespace DashboardBackEnd.Auth.Oidc;

// Bound once per provider as named options (Oidc:Google, Oidc:Microsoft). Not validated on start:
// a fresh clone has no OAuth apps, and the rest of the API must still boot.
public class OidcProviderOptions
{
    public const string SectionName = "Oidc";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Must match a redirect URI registered on the provider. Left unset, it is derived from the
    // incoming request, which only works when the API is reached directly.
    public string? CallbackUrl { get; set; }

    // Microsoft only: which accounts may sign in. "common" accepts work/school and personal ones.
    public string Tenant { get; set; } = "common";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
