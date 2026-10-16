using System.Net;
using System.Text;
using System.Web;

namespace DashboardBackEnd.Tests;

// Stands in for GitHub's OAuth and API hosts so the suite never touches the network, and mirrors
// the answer that trips people up: a refused code exchange comes back as HTTP 200 with an
// "error" field, not as a 4xx.
//
// The code carries the account it belongs to, so every test can link a GitHub account of its own.
// That matters because a GitHub account links to one platform user, and the test database persists.
//
// Two logins behave badly on purpose: RateLimitedLogin exhausts its budget, and any token that is
// not one we issued is refused, which is what a revoked link looks like.
public class FakeGitHubHandler : HttpMessageHandler
{
    public const string CodePrefix = "valid-code-";
    public const string DefaultLogin = "octocat";

    // Its repository and event endpoints answer 403 with an exhausted budget.
    public const string RateLimitedLogin = "ratelimited";

    // What the fake publishes for every well-behaved login.
    public const int Followers = 1234;
    public const int PublicRepos = 8;

    // Commits inside the windows the widget tests use: 3 (an hour ago) + 4 (yesterday) = 7,
    // plus 5 from ten days ago, which only a window longer than ten days reaches.
    public const int CommitsLastWeek = 7;
    public const int CommitsLastMonth = 12;

    public static string CodeFor(string login) => CodePrefix + login;

    public static string TokenFor(string login) => "gho_" + login;

    // Deterministic, so a test can assert the id that was stored as the external login — and
    // injective, which matters more than it looks: an external login is unique per provider key,
    // so two logins sharing an id would make the second link silently store no row. Summing the
    // characters gave a few hundred possible ids, and the test database keeps every one it has
    // ever seen.
    public static string AccountIdFor(string login)
    {
        ulong hash = 14695981039346656037; // FNV-1a, 64-bit

        foreach (var b in Encoding.UTF8.GetBytes(login))
        {
            hash = (hash ^ b) * 1099511628211;
        }

        return (hash & long.MaxValue).ToString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;

        if (path == "/login/oauth/access_token")
        {
            var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
            var code = form["code"] ?? "";

            if (!code.StartsWith(CodePrefix))
            {
                return Json(HttpStatusCode.OK, """
                {"error":"bad_verification_code",
                "error_description":"The code passed is incorrect or expired."}
                """);
            }

            var token = TokenFor(code[CodePrefix.Length..]);
            return Json(HttpStatusCode.OK, """
            {"access_token":"%TOKEN%","token_type":"bearer","scope":"read:user"}
            """.Replace("%TOKEN%", token));
        }

        if (LoginOf(request) is not { } login)
        {
            return Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        }

        if (path == "/user")
        {
            return Json(HttpStatusCode.OK, Profile(login));
        }

        if (path == $"/users/{login}/repos")
        {
            if (login == RateLimitedLogin) return RateLimited();

            var count = int.TryParse(HttpUtility.ParseQueryString(uri.Query)["per_page"], out var n) ? n : 30;
            return Json(HttpStatusCode.OK, Repos(login, count));
        }

        if (path == $"/users/{login}/events/public")
        {
            if (login == RateLimitedLogin) return RateLimited();

            return Json(HttpStatusCode.OK, Events(login));
        }

        // Push payloads without a size are counted by comparing the two ends of the push.
        if (path.StartsWith($"/repos/{login}/repo-1/compare/"))
        {
            var heads = Uri.UnescapeDataString(path[path.LastIndexOf('/')..]).Split("...");
            return heads.Length == 2 && PushSizes.TryGetValue(heads[1], out var size)
                ? Json(HttpStatusCode.OK, $$"""{"total_commits":{{size}}}""")
                : Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        }

        return Json(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
    }

    // Every token this fake issues is "gho_" + the login, so the token names the account it belongs
    // to. Anything else is a credential GitHub would refuse.
    private static string? LoginOf(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter ?? "";
        return token.StartsWith("gho_") ? token["gho_".Length..] : null;
    }

    private static string Profile(string login) => """
    {"id":%ID%,"login":"%LOGIN%","name":"Test Account",
    "avatar_url":"https://avatars.example/%LOGIN%.png","html_url":"https://github.com/%LOGIN%",
    "public_repos":%REPOS%,"public_gists":3,"followers":%FOLLOWERS%,"following":9,
    "bio":"Built by a test","company":"@epitech","location":"Lille",
    "created_at":"2020-01-02T03:04:05Z"}
    """
        .Replace("%ID%", AccountIdFor(login))
        .Replace("%LOGIN%", login)
        .Replace("%REPOS%", PublicRepos.ToString())
        .Replace("%FOLLOWERS%", Followers.ToString());

    private static string Repos(string login, int count)
    {
        var languages = new[] { "C#", "JavaScript", "Rust" };

        var repos = Enumerable.Range(1, Math.Min(count, 10)).Select(i => """
        {"name":"repo-%I%","full_name":"%LOGIN%/repo-%I%","description":"Repository number %I%",
        "language":"%LANG%","stargazers_count":%STARS%,"forks_count":%I%,
        "html_url":"https://github.com/%LOGIN%/repo-%I%","pushed_at":"%PUSHED%","fork":false}
        """
            .Replace("%I%", i.ToString())
            .Replace("%LOGIN%", login)
            .Replace("%LANG%", languages[(i - 1) % languages.Length])
            .Replace("%STARS%", (i * 10).ToString())
            .Replace("%PUSHED%", Iso(DateTimeOffset.UtcNow.AddDays(-i))));

        return "[" + string.Join(",", repos) + "]";
    }

    // What the compare endpoint answers for the head of each push that carries no size.
    private static readonly Dictionary<string, int> PushSizes = new() { ["head-1"] = 3, ["head-2"] = 4 };

    // Newest first, as GitHub returns them. The ten-day-old push is what makes a window testable.
    // The recent pushes have the payload GitHub sends since 2025, without a size; the old one keeps
    // the size it used to carry, so both shapes stay covered.
    private static string Events(string login)
    {
        var events = new[]
        {
            CountlessPush(login, 1, DateTimeOffset.UtcNow.AddHours(-1)),
            CountlessPush(login, 2, DateTimeOffset.UtcNow.AddDays(-1)),
            Other(login, 3, "PullRequestEvent", "opened", DateTimeOffset.UtcNow.AddDays(-2)),
            Other(login, 4, "WatchEvent", "started", DateTimeOffset.UtcNow.AddDays(-3)),
            Push(login, 5, 5, DateTimeOffset.UtcNow.AddDays(-10)),
        };

        return "[" + string.Join(",", events) + "]";
    }

    private static string Push(string login, int id, int size, DateTimeOffset at) => """
    {"id":"%ID%","type":"PushEvent","repo":{"name":"%LOGIN%/repo-1"},
    "payload":{"size":%SIZE%,"distinct_size":%SIZE%},"created_at":"%AT%"}
    """
        .Replace("%ID%", id.ToString())
        .Replace("%LOGIN%", login)
        .Replace("%SIZE%", size.ToString())
        .Replace("%AT%", Iso(at));

    private static string CountlessPush(string login, int id, DateTimeOffset at) => """
    {"id":"%ID%","type":"PushEvent","repo":{"name":"%LOGIN%/repo-1"},
    "payload":{"repository_id":1,"push_id":%ID%,"ref":"refs/heads/main","head":"head-%ID%","before":"before-%ID%"},
    "created_at":"%AT%"}
    """
        .Replace("%ID%", id.ToString())
        .Replace("%LOGIN%", login)
        .Replace("%AT%", Iso(at));

    private static string Other(string login, int id, string type, string action, DateTimeOffset at) => """
    {"id":"%ID%","type":"%TYPE%","repo":{"name":"%LOGIN%/repo-1"},
    "payload":{"action":"%ACTION%","number":1},"created_at":"%AT%"}
    """
        .Replace("%ID%", id.ToString())
        .Replace("%TYPE%", type)
        .Replace("%LOGIN%", login)
        .Replace("%ACTION%", action)
        .Replace("%AT%", Iso(at));

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    // GitHub reports an exhausted budget as a 403 whose headers say the quota is gone.
    private static HttpResponseMessage RateLimited()
    {
        var response = Json(HttpStatusCode.Forbidden,
            """{"message":"API rate limit exceeded for user ID 1."}""");

        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset",
            DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString());

        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
