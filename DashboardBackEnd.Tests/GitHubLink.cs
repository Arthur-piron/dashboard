using System.Net;
using System.Text.Json;
using System.Web;

namespace DashboardBackEnd.Tests;

// Linking a GitHub account takes two clients: the authenticated one that starts the flow, and a
// redirect-following-disabled one standing in for the browser GitHub sends back. Both the OAuth
// tests and the widget tests need that dance, so it lives here once.
internal static class GitHubLink
{
    public const string Authorize = "/services/github/oauth/authorize";
    public const string Callback = "/services/github/oauth/callback";

    // A GitHub account links to one platform user and the test database persists between runs,
    // so every test links a login of its own.
    public static string NewLogin() => $"octo-{Guid.NewGuid():N}"[..14];

    public static async Task<string> StateAsync(HttpClient api)
    {
        var response = await api.GetAsync(Authorize);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var url = new Uri(body.GetProperty("url").GetString()!);

        return HttpUtility.ParseQueryString(url.Query)["state"]!;
    }

    public static Task<HttpResponseMessage> CallbackAsync(HttpClient browser, string query) =>
        browser.GetAsync($"{Callback}?{query}");

    // Completes a whole flow and returns the login that was linked.
    public static async Task<string> LinkAsync(HttpClient api, HttpClient browser, string? login = null)
    {
        login ??= NewLogin();

        var response = await CallbackAsync(
            browser, $"code={FakeGitHubHandler.CodeFor(login)}&state={await StateAsync(api)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("github=linked", response.Headers.Location!.ToString());

        return login;
    }
}
