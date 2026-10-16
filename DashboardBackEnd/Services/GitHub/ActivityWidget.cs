using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.GitHub;

public class ActivityWidget : IWidget
{
    private readonly GitHubClient _github;

    public ActivityWidget(GitHubClient github)
    {
        _github = github;
    }

    public string Key => "github.activity";
    public string ServiceKey => GitHubService.ServiceKey;
    public string Name => "Recent activity";

    public string Description =>
        "Recent public activity of the linked account, and the commits it pushed over the period. "
        + "GitHub only publishes public events, capped at 300 over 90 days, so the commit count is an approximation.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("count", ParamType.Integer, Required: false,
            Description: "How many recent events to list (1-10)", Min: 1, Max: 10),
        new("days", ParamType.Integer, Required: false,
            Description: "Length of the period the commit count covers, in days (1-30)", Min: 1, Max: 30),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var token = GitHubService.TokenOf(credentials);
        var count = config.TryGetProperty("count", out var c) ? c.GetInt32() : 5;
        var days = config.TryGetProperty("days", out var d) ? d.GetInt32() : 7;

        var login = (await _github.GetProfileAsync(token, ct)).Login;
        var events = await _github.ListPublicEventsAsync(token, login, ct);

        var firstDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(days - 1));
        var window = (await Task.WhenAll(events
            .Where(e => DateOnly.FromDateTime(e.CreatedAt.UtcDateTime) >= firstDay)
            .Select(e => WithCommitCountAsync(e, token, ct))))
            .ToList();

        // One entry per day including the quiet ones, so a chart needs no gap filling.
        var perDay = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new
            {
                Date = day.ToString("yyyy-MM-dd"),
                Commits = window
                    .Where(e => DateOnly.FromDateTime(e.CreatedAt.UtcDateTime) == day)
                    .Sum(e => e.Commits),
            })
            .ToList();

        return new
        {
            Login = login,
            Days = days,
            Commits = window.Sum(e => e.Commits),
            PerDay = perDay,
            Events = window
                .Take(count)
                .Select(e => new { e.Id, e.Type, e.Repo, e.Summary, e.Commits, e.CreatedAt })
                .ToList(),
        };
    }

    // Only pushes whose payload lost its count cost a request, and only those inside the window.
    private async Task<GitHubClient.GitHubEvent> WithCommitCountAsync(
        GitHubClient.GitHubEvent e, string token, CancellationToken ct)
    {
        if (e.Before is null || e.Head is null) return e;

        var commits = await _github.CountPushCommitsAsync(token, e.Repo, e.Before, e.Head, ct);
        return e with { Commits = commits, Summary = GitHubClient.PushSummary(commits) };
    }
}
