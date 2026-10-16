using System.Net;
using System.Net.Http.Json;

namespace DashboardBackEnd.Tests;

public static class TestUser
{
    public const string Password = "Password123";

    // `email` is for tests that need a known address, such as the configured administrator.
    public static async Task<string> CreateAndLoginAsync(
        HttpClient api, MailpitClient mailpit, string? email = null)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var knownAddress = email is not null;
        email ??= $"user-{id}@example.com";

        var register = await api.PostAsJsonAsync("/auth/register", new { email, username = $"user{id}", password = Password });
        // a known address is registered again by every test of the class that uses it
        var alreadyRegistered = knownAddress && register.StatusCode == HttpStatusCode.BadRequest
            && (await register.Content.ReadAsStringAsync()).Contains("DuplicateEmail");
        if (!alreadyRegistered) await EnsureSuccessAsync("register", register);

        var link = await mailpit.WaitForLinkAsync(email);
        await EnsureSuccessAsync("confirm", await api.GetAsync($"/auth/confirm{new Uri(link).Query}"));

        var login = await api.PostAsJsonAsync("/auth/login", new { email, password = Password });
        await EnsureSuccessAsync("login", login);
        var body = await login.Content.ReadFromJsonAsync<TokenResponse>();

        if (string.IsNullOrEmpty(body?.Token))
        {
            throw new InvalidOperationException($"Test user setup: login for {email} returned no token.");
        }

        return body.Token;
    }

    // a failed setup step would otherwise surface later as an unrelated 401
    private static async Task EnsureSuccessAsync(string step, HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"Test user setup: {step} returned {(int)response.StatusCode}. {content}");
    }

    private record TokenResponse(string Token);
}
