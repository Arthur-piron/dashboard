using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.GitHub;

public class ReposWidget : IWidget
{
    // The only values GitHub's repository listing accepts; there is no sort by stars.
    private static readonly string[] Sorts = ["pushed", "updated", "created", "full_name"];

    private readonly GitHubClient _github;

    public ReposWidget(GitHubClient github)
    {
        _github = github;
    }

    public string Key => "github.repos";
    public string ServiceKey => GitHubService.ServiceKey;
    public string Name => "Repositories";
    public string Description => "Public repositories of the linked account, with their language, stars and forks.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("count", ParamType.Integer, Required: false,
            Description: "How many repositories to return (1-10)", Min: 1, Max: 10),
        new("sort", ParamType.String, Required: false,
            Description: "Which order to list them in", Options: Sorts),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var token = GitHubService.TokenOf(credentials);
        var count = config.TryGetProperty("count", out var c) ? c.GetInt32() : 5;
        var sort = config.TryGetProperty("sort", out var s) ? s.GetString() ?? "pushed" : "pushed";

        // Resolved through GetProfileAsync rather than GetAccountAsync so a revoked token reaches
        // the user as "link again" instead of a generic failure.
        var login = (await _github.GetProfileAsync(token, ct)).Login;
        var repos = await _github.ListReposAsync(token, login, count, sort, ct);

        return new
        {
            Login = login,
            Sort = sort,
            Count = count,
            Repos = repos,
        };
    }
}
