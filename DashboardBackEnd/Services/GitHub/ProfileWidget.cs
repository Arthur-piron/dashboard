using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.GitHub;

public class ProfileWidget : IWidget
{
    private readonly GitHubClient _github;

    public ProfileWidget(GitHubClient github)
    {
        _github = github;
    }

    public string Key => "github.profile";
    public string ServiceKey => GitHubService.ServiceKey;
    public string Name => "GitHub profile";
    public string Description => "The linked GitHub account: repositories, gists, followers and who it follows.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("detail", ParamType.String, Required: false,
            Description: "How much of the profile to return. Defaults to compact.",
            Options: ["compact", "full"]),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var token = GitHubService.TokenOf(credentials);
        var detail = config.TryGetProperty("detail", out var d) ? d.GetString() ?? "compact" : "compact";

        var profile = await _github.GetProfileAsync(token, ct);

        if (detail != "full")
        {
            return new
            {
                profile.Login,
                profile.Name,
                profile.AvatarUrl,
                profile.HtmlUrl,
                profile.PublicRepos,
                profile.PublicGists,
                profile.Followers,
                profile.Following,
                Detail = "compact",
            };
        }

        return new
        {
            profile.Login,
            profile.Name,
            profile.AvatarUrl,
            profile.HtmlUrl,
            profile.PublicRepos,
            profile.PublicGists,
            profile.Followers,
            profile.Following,
            Detail = "full",
            profile.Bio,
            profile.Company,
            profile.Location,
            profile.CreatedAt,
        };
    }
}
