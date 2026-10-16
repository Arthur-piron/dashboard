using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DashboardBackEnd.Tests;

internal record OidcAuthorization(Uri Url, string State, string Nonce, string Challenge);

// Plays the browser through a sign-in: one client keeps the binding cookie from /authorize to
// /callback and does not follow the final redirect, so the test can read where it points.
internal static class OidcLogin
{
    public static HttpClient Browser(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // The test database persists between runs, so every identity is new.
    public static string NewSub() => Guid.NewGuid().ToString("N");

    public static string NewEmail() => $"oidc-{Guid.NewGuid():N}"[..17] + "@example.com";

    public static async Task<OidcAuthorization> AuthorizeAsync(HttpClient browser, string provider)
    {
        var response = await browser.GetAsync($"/auth/oidc/{provider}/authorize");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var url = new Uri(body.GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);

        return new OidcAuthorization(url, query["state"]!, query["nonce"]!, query["code_challenge"]!);
    }

    public static Task<HttpResponseMessage> CallbackAsync(HttpClient browser, string provider, string query) =>
        browser.GetAsync($"/auth/oidc/{provider}/callback?{query}");

    public static Task<HttpResponseMessage> CompleteAsync(
        HttpClient browser, string provider, OidcAuthorization authorization, FakeIdentity identity) =>
        CallbackAsync(browser, provider,
            $"code={Uri.EscapeDataString(FakeOidcHandler.CodeFor(identity))}&state={Uri.EscapeDataString(authorization.State)}");

    // A whole sign-in that must succeed; returns our JWT.
    public static async Task<string> SignInAsync(
        HttpClient browser, string provider, Func<OidcAuthorization, FakeIdentity> identity)
    {
        var authorization = await AuthorizeAsync(browser, provider);
        return TokenFrom(await CompleteAsync(browser, provider, authorization, identity(authorization)));
    }

    public static string TokenFrom(HttpResponseMessage response)
    {
        var location = Location(response);
        Assert.Contains("/auth/callback#", location);

        var fragment = HttpUtility.ParseQueryString(location[(location.IndexOf('#') + 1)..]);
        return fragment["token"]!;
    }

    public static string FailureReason(HttpResponseMessage response)
    {
        var location = new Uri(Location(response));
        Assert.Equal("/login", location.AbsolutePath);

        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("failed", query["oidc"]);
        return query["reason"]!;
    }

    private static string Location(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }
}
