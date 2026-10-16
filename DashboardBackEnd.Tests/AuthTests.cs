using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DashboardBackEnd.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public AuthTests(ApiFactory factory)
    {
        _api = factory.CreateClient();
    }

    private static (string Email, string Username) NewIdentity()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        return ($"user-{id}@example.com", $"user{id}");
    }

    private const string Password = "Password123";

    private async Task<HttpResponseMessage> RegisterAsync(string email, string username, string password = Password)
        => await _api.PostAsJsonAsync("/auth/register", new { email, username, password });

    private async Task<HttpResponseMessage> LoginAsync(string email, string password = Password)
        => await _api.PostAsJsonAsync("/auth/login", new { email, password });

    private async Task ConfirmFromEmailAsync(string email)
    {
        var link = await _mailpit.WaitForLinkAsync(email);
        var query = new Uri(link).Query; // "?userId=...&token=..."
        var response = await _api.GetAsync($"/auth/confirm{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_returns_202_and_sends_a_confirmation_email()
    {
        var (email, username) = NewIdentity();

        var response = await RegisterAsync(email, username);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var link = await _mailpit.WaitForLinkAsync(email);
        Assert.Contains("/confirm?userId=", link);
        Assert.Contains("&token=", link);
    }

    [Fact]
    public async Task Register_with_an_email_already_taken_returns_400()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);

        var response = await RegisterAsync(email, username + "b");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("DuplicateEmail", body);
    }

    [Fact]
    public async Task Register_with_a_weak_password_returns_400()
    {
        var (email, username) = NewIdentity();

        var response = await RegisterAsync(email, username, password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PasswordTooShort", body);
    }

    [Fact]
    public async Task Login_before_confirming_the_email_returns_403()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);

        var response = await LoginAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("email_not_confirmed", body);
    }

    [Fact]
    public async Task Confirm_then_login_then_me_succeeds()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);
        await ConfirmFromEmailAsync(email);

        var login = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Token));
        Assert.True(body.ExpiresAt > DateTime.UtcNow);

        var me = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        var meResponse = await _api.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meBody = await meResponse.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(username, meBody!.Username);
        Assert.Equal(email, meBody.Email);
        Assert.Empty(meBody.Roles);
    }

    [Fact]
    public async Task Confirming_twice_is_not_an_error()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);
        var link = await _mailpit.WaitForLinkAsync(email);
        var query = new Uri(link).Query;

        var first = await _api.GetAsync($"/auth/confirm{query}");
        var second = await _api.GetAsync($"/auth/confirm{query}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Confirm_with_a_garbage_token_returns_400()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);
        var link = await _mailpit.WaitForLinkAsync(email);
        var userId = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["userId"];

        var response = await _api.GetAsync($"/auth/confirm?userId={userId}&token=not-a-real-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_returns_401()
    {
        var (email, username) = NewIdentity();
        await RegisterAsync(email, username);
        await ConfirmFromEmailAsync(email);

        var response = await LoginAsync(email, password: "WrongPassword1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_returns_401()
    {
        var response = await LoginAsync("nobody@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_a_token_returns_401()
    {
        var response = await _api.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record LoginResponse(string Token, DateTime ExpiresAt);
    private record MeResponse(string Id, string Username, string Email, List<string> Roles);
}
