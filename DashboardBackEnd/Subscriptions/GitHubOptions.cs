namespace DashboardBackEnd.Subscriptions;

// Not validated on start: a fresh clone has no OAuth app, and the rest of the API must still boot.
// The endpoints answer 503 while this is unset.
public class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Must match the callback registered on the OAuth app. Left unset, it is derived from the
    // incoming request, which only works when the API is reached directly.
    public string? CallbackUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
