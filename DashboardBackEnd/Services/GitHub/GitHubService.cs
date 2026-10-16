using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.GitHub;

public class GitHubService : IService
{
    public const string ServiceKey = "github";

    public string Key => ServiceKey;
    public string Name => "GitHub";
    public string Description => "Your GitHub account, linked once through OAuth. Nothing to paste.";
    public AuthKind Auth => AuthKind.Oauth;

    public static string TokenOf(ServiceCredentials? credentials) =>
        credentials?.AccessToken is { Length: > 0 } token
            ? token
            : throw new WidgetDataException("Link your GitHub account before using this widget.");
}
