using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.ApiClients;

public class GitHubClient
{
    public const string HttpClientName = "github";

    // Enough to identify the account. Public repositories need no scope at all.
    public const string RequestedScope = "read:user";

    private const string AuthorizeEndpoint = "https://github.com/login/oauth/authorize";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string ApiBase = "https://api.github.com";
    private const string UserEndpoint = ApiBase + "/user";

    private readonly IHttpClientFactory _http;

    public GitHubClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public record AccessToken(string Value, string? Scope);

    public record GitHubAccount(string Id, string Login, string? Name);

    public record GitHubProfile(
        string Login,
        string? Name,
        string AvatarUrl,
        string HtmlUrl,
        int PublicRepos,
        int PublicGists,
        int Followers,
        int Following,
        string? Bio,
        string? Company,
        string? Location,
        string? CreatedAt);

    public record GitHubRepo(
        string Name,
        string FullName,
        string? Description,
        string? Language,
        int Stars,
        int Forks,
        string HtmlUrl,
        string? PushedAt,
        bool IsFork);

    public record GitHubEvent(
        string Id,
        string Type,
        string Repo,
        string Summary,
        int Commits,
        DateTimeOffset CreatedAt,
        // Set only on a push whose payload no longer carries a commit count; see CountPushCommitsAsync.
        string? Before = null,
        string? Head = null);

    public static string AuthorizeUrl(string clientId, string callbackUrl, string state) =>
        $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}"
        + $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}"
        + $"&scope={Uri.EscapeDataString(RequestedScope)}"
        + $"&state={Uri.EscapeDataString(state)}";

    // GitHub answers a refused exchange with HTTP 200 and an "error" field, so the outcome is
    // read out of the body rather than the status code.
    public async Task<AccessToken?> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string callbackUrl, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = callbackUrl,
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        return GetString(doc.RootElement, "access_token") is { Length: > 0 } token
            ? new AccessToken(token, GetString(doc.RootElement, "scope"))
            : null;
    }

    // Returns null rather than throwing because its caller is the OAuth callback, which turns any
    // failure into a redirect. The widget-facing methods below throw instead, so a failure can
    // become a 422 the user can read.
    public async Task<GitHubAccount?> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var account = doc.RootElement;

        if (GetString(account, "login") is not { Length: > 0 } login) return null;

        return new GitHubAccount(
            Id: account.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                ? id.GetInt64().ToString()
                : login,
            Login: login,
            Name: GetString(account, "name"));
    }

    public async Task<GitHubProfile> GetProfileAsync(string accessToken, CancellationToken ct)
    {
        using var doc = await GetAsync(UserEndpoint, accessToken, ct);
        var user = doc.RootElement;

        if (GetString(user, "login") is not { Length: > 0 } login)
        {
            throw new WidgetDataException("GitHub returned no account for this link.");
        }

        return new GitHubProfile(
            Login: login,
            Name: GetString(user, "name"),
            AvatarUrl: GetString(user, "avatar_url") ?? "",
            HtmlUrl: GetString(user, "html_url") ?? $"https://github.com/{login}",
            PublicRepos: GetInt(user, "public_repos"),
            PublicGists: GetInt(user, "public_gists"),
            Followers: GetInt(user, "followers"),
            Following: GetInt(user, "following"),
            Bio: GetString(user, "bio"),
            Company: GetString(user, "company"),
            Location: GetString(user, "location"),
            CreatedAt: GetString(user, "created_at"));
    }

    public async Task<IReadOnlyList<GitHubRepo>> ListReposAsync(
        string accessToken, string login, int count, string sort, CancellationToken ct)
    {
        var url = $"{ApiBase}/users/{Uri.EscapeDataString(login)}/repos"
            + $"?sort={Uri.EscapeDataString(sort)}&per_page={count}&type=owner";

        using var doc = await GetAsync(url, accessToken, ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new WidgetDataException("GitHub returned no repositories for this account.");
        }

        return doc.RootElement.EnumerateArray()
            .Take(count)
            .Select(repo => new GitHubRepo(
                Name: GetString(repo, "name") ?? "",
                FullName: GetString(repo, "full_name") ?? "",
                Description: GetString(repo, "description"),
                Language: GetString(repo, "language"),
                Stars: GetInt(repo, "stargazers_count"),
                Forks: GetInt(repo, "forks_count"),
                HtmlUrl: GetString(repo, "html_url") ?? "",
                PushedAt: GetString(repo, "pushed_at"),
                IsFork: repo.TryGetProperty("fork", out var fork) && fork.ValueKind == JsonValueKind.True))
            .ToList();
    }

    // Newest first, as GitHub returns them. The window and the commit total are the widget's business.
    public async Task<IReadOnlyList<GitHubEvent>> ListPublicEventsAsync(
        string accessToken, string login, CancellationToken ct)
    {
        var url = $"{ApiBase}/users/{Uri.EscapeDataString(login)}/events/public?per_page=100";

        using var doc = await GetAsync(url, accessToken, ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        return doc.RootElement.EnumerateArray()
            .Select(ToEvent)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    private static GitHubEvent? ToEvent(JsonElement element)
    {
        var type = GetString(element, "type");
        if (type is null) return null;

        if (!DateTimeOffset.TryParse(GetString(element, "created_at"), out var createdAt)) return null;

        var payload = element.TryGetProperty("payload", out var p) ? p : default;
        var repo = element.TryGetProperty("repo", out var r) ? GetString(r, "name") ?? "" : "";

        // GitHub dropped "size" from push payloads in 2025, leaving only the commits on either side.
        // Those pushes keep before and head so the widget can ask for the count in its window only.
        if (type == "PushEvent"
            && payload.ValueKind == JsonValueKind.Object
            && !payload.TryGetProperty("size", out _)
            && GetString(payload, "before") is { Length: > 0 } before
            && GetString(payload, "head") is { Length: > 0 } head)
        {
            return new GitHubEvent(
                Id: GetString(element, "id") ?? "",
                Type: type,
                Repo: repo,
                Summary: PushSummary(1),
                Commits: 1,
                CreatedAt: createdAt,
                Before: before,
                Head: head);
        }

        var commits = type == "PushEvent" && payload.ValueKind == JsonValueKind.Object
            ? GetInt(payload, "size")
            : 0;

        return new GitHubEvent(
            Id: GetString(element, "id") ?? "",
            Type: type,
            Repo: repo,
            Summary: Summarise(type, payload, commits),
            Commits: commits,
            CreatedAt: createdAt);
    }

    // The number of commits a push brought, from the comparison of what the branch pointed at
    // before and after it. A push GitHub cannot compare (a new branch starts from 0000..., a force
    // push may point at a commit that is gone) counts as one commit rather than failing the widget.
    public async Task<int> CountPushCommitsAsync(
        string accessToken, string repo, string before, string head, CancellationToken ct)
    {
        var path = string.Join("/", repo.Split('/').Select(Uri.EscapeDataString));
        var url = $"{ApiBase}/repos/{path}/compare/{Uri.EscapeDataString(before)}...{Uri.EscapeDataString(head)}";

        try
        {
            using var doc = await GetAsync(url, accessToken, ct);
            var total = GetInt(doc.RootElement, "total_commits");
            return total > 0 ? total : 1;
        }
        catch (Exception e) when (e is WidgetDataException or HttpRequestException or JsonException)
        {
            return 1;
        }
    }

    public static string PushSummary(int commits) =>
        commits == 1 ? "pushed 1 commit" : $"pushed {commits} commits";

    // A readable sentence per event, so the front end does not have to learn GitHub's event taxonomy.
    private static string Summarise(string type, JsonElement payload, int commits)
    {
        var action = payload.ValueKind == JsonValueKind.Object ? GetString(payload, "action") : null;

        return type switch
        {
            "PushEvent" => PushSummary(commits),
            "PullRequestEvent" => $"{action ?? "updated"} a pull request",
            "PullRequestReviewEvent" => "reviewed a pull request",
            "PullRequestReviewCommentEvent" => "commented on a pull request",
            "IssuesEvent" => $"{action ?? "updated"} an issue",
            "IssueCommentEvent" => "commented on an issue",
            "WatchEvent" => "starred the repository",
            "ForkEvent" => "forked the repository",
            "CreateEvent" => $"created a {GetString(payload, "ref_type") ?? "reference"}",
            "DeleteEvent" => $"deleted a {GetString(payload, "ref_type") ?? "reference"}",
            "ReleaseEvent" => "published a release",
            "PublicEvent" => "made the repository public",
            "MemberEvent" => "added a collaborator",
            "CommitCommentEvent" => "commented on a commit",
            "GollumEvent" => "edited the wiki",
            _ => type.EndsWith("Event") ? type[..^"Event".Length] : type,
        };
    }

    // GitHub answers a request a user can act on with a status of its own. Those become
    // WidgetDataException so all three widgets word them identically; anything else is ours.
    private async Task<JsonDocument> GetAsync(string url, string accessToken, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new WidgetDataException(
                    "GitHub no longer accepts this account link. Unlink GitHub and link it again."),
                HttpStatusCode.TooManyRequests => RateLimited(response),
                HttpStatusCode.Forbidden => IsRateLimited(response)
                    ? RateLimited(response)
                    : new WidgetDataException("GitHub refused this request."),
                HttpStatusCode.NotFound => new WidgetDataException(
                    "GitHub has no public data for this account."),
                _ => new HttpRequestException($"GitHub returned {(int)response.StatusCode}."),
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    // A 403 is how GitHub reports an exhausted budget as well as a plain refusal; the header
    // is what tells them apart.
    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
        && values.FirstOrDefault() == "0";

    private static WidgetDataException RateLimited(HttpResponseMessage response)
    {
        var reset = response.Headers.TryGetValues("x-ratelimit-reset", out var values)
            && long.TryParse(values.FirstOrDefault(), out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("HH:mm")
                : null;

        return new WidgetDataException(reset is null
            ? "GitHub's rate limit for this account is used up. Try again later."
            : $"GitHub's rate limit for this account is used up. It resets at {reset} UTC.");
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
