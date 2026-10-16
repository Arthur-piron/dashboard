using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DashboardBackEnd.Auth;
using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

public class AdminTests : IClassFixture<ApiFactoryWithAdmin>, IAsyncLifetime
{
    private readonly ApiFactoryWithAdmin _factory;
    private readonly HttpClient _admin;
    private readonly MailpitClient _mailpit = new();

    private string _adminId = "";

    public AdminTests(ApiFactoryWithAdmin factory)
    {
        _factory = factory;
        _admin = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Registering the configured address is what promotes it.
        var token = await TestUser.CreateAndLoginAsync(_admin, _mailpit, _factory.AdminEmail);
        _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _adminId = (await JsonAsync(await _admin.GetAsync("/auth/me"))).GetProperty("id").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<(HttpClient Client, string Id)> NewUserAsync()
    {
        var client = _factory.CreateClient();
        var token = await TestUser.CreateAndLoginAsync(client, _mailpit);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var id = (await JsonAsync(await client.GetAsync("/auth/me"))).GetProperty("id").GetString()!;
        return (client, id);
    }

    private async Task<JsonElement> ListAsync() => await JsonAsync(await _admin.GetAsync("/admin/users"));

    private static JsonElement Find(JsonElement users, string id) =>
        users.EnumerateArray().Single(u => u.GetProperty("id").GetString() == id);

    [Fact]
    public async Task The_configured_address_becomes_an_administrator_when_it_registers()
    {
        var me = await JsonAsync(await _admin.GetAsync("/auth/me"));

        Assert.Equal(_factory.AdminEmail, me.GetProperty("email").GetString());
        Assert.Contains("Admin", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task An_ordinary_user_gets_no_role()
    {
        var (client, _) = await NewUserAsync();

        var me = await JsonAsync(await client.GetAsync("/auth/me"));

        Assert.Empty(me.GetProperty("roles").EnumerateArray());
    }

    [Fact]
    public async Task Admin_routes_need_a_token()
    {
        using var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_routes_are_forbidden_to_ordinary_users_not_hidden_from_them()
    {
        var (client, id) = await NewUserAsync();

        foreach (var response in new[]
                 {
                     await client.GetAsync("/admin/users"),
                     await client.DeleteAsync($"/admin/users/{id}"),
                     await client.PostAsync($"/admin/users/{id}/admin", null),
                     await client.DeleteAsync($"/admin/users/{id}/admin"),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_listing_shows_an_account_with_what_it_owns()
    {
        var (client, id) = await NewUserAsync();

        await client.PostAsJsonAsync("/widget-instances", new
        {
            widgetKey = "weather.current",
            config = new { city = "Lille" },
            refreshRateSeconds = 300,
        });

        var user = Find(await ListAsync(), id);

        Assert.True(user.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(1, user.GetProperty("widgetInstances").GetInt32());
        Assert.Equal(0, user.GetProperty("subscriptions").GetInt32());
        Assert.Empty(user.GetProperty("roles").EnumerateArray());
        Assert.Contains("Admin", Find(await ListAsync(), _adminId).GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task The_listing_carries_no_password_hash_and_no_credential()
    {
        await NewUserAsync();

        var body = await (await _admin.GetAsync("/admin/users")).Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "passwordHash", "PasswordHash", "securityStamp", "protectedCredentials" })
        {
            Assert.DoesNotContain(forbidden, body);
        }
    }

    [Fact]
    public async Task Deleting_a_user_takes_their_widgets_and_subscriptions_with_them()
    {
        var (client, id) = await NewUserAsync();

        await client.PostAsJsonAsync("/widget-instances", new
        {
            widgetKey = "weather.current",
            config = new { city = "Lille" },
            refreshRateSeconds = 300,
        });

        var response = await _admin.DeleteAsync($"/admin/users/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Users.AnyAsync(u => u.Id == id));
        Assert.False(await db.WidgetInstances.AnyAsync(w => w.UserId == id));
        Assert.False(await db.ServiceSubscriptions.AnyAsync(s => s.UserId == id));
    }

    [Fact]
    public async Task Deleting_an_unknown_user_returns_404()
    {
        var (_, id) = await NewUserAsync();
        await _admin.DeleteAsync($"/admin/users/{id}");

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/admin/users/{id}")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_delete_their_own_account()
    {
        var response = await _admin.DeleteAsync($"/admin/users/{_adminId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot delete their own account", await response.Content.ReadAsStringAsync());

        var me = await _admin.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_revoke_their_own_role()
    {
        var response = await _admin.DeleteAsync($"/admin/users/{_adminId}/admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot revoke their own role", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Granting_the_role_lets_the_promoted_user_in_once_they_log_in_again()
    {
        var (client, id) = await NewUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/admin/users/{id}/admin", null)).StatusCode);
        Assert.Contains("Admin", Find(await ListAsync(), id).GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()));

        // The role travels in the JWT, so only a token minted after the grant carries it.
        var promoted = await TokenForAsync(id);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", promoted);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Revoking_the_role_shuts_the_door_on_the_next_token()
    {
        var (client, id) = await NewUserAsync();
        await _admin.PostAsync($"/admin/users/{id}/admin", null);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/admin/users/{id}/admin")).StatusCode);
        Assert.Empty(Find(await ListAsync(), id).GetProperty("roles").EnumerateArray());

        var after = await TokenForAsync(id);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", after);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Granting_and_revoking_are_idempotent()
    {
        var (_, id) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/admin/users/{id}/admin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/admin/users/{id}/admin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/admin/users/{id}/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/admin/users/{id}/admin")).StatusCode);
    }

    [Fact]
    public async Task Acting_on_an_unknown_id_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsync("/admin/users/nobody/admin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync("/admin/users/nobody/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync("/admin/users/nobody")).StatusCode);
    }

    [Fact]
    public async Task An_unconfirmed_account_is_promoted_only_once_it_confirms()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        using (var anonymous = _factory.CreateClient())
        {
            await anonymous.PostAsJsonAsync("/auth/register",
                new { email, username = $"adm{Guid.NewGuid():N}"[..12], password = TestUser.Password });
        }

        // A fresh start configured for that address: the seed finds it, but it owns nothing yet.
        await using var restarted = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Email"] = email })));

        Assert.False(await IsAdminAsync(restarted, email));

        using var client = restarted.CreateClient();
        var link = await _mailpit.WaitForLinkAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/auth/confirm{new Uri(link).Query}")).StatusCode);

        Assert.True(await IsAdminAsync(restarted, email));
    }

    private static async Task<bool> IsAdminAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.IsInRoleAsync((await users.FindByEmailAsync(email))!, AppRoles.Admin);
    }

    private async Task<string> TokenForAsync(string userId)
    {
        var email = Find(await ListAsync(), userId).GetProperty("email").GetString();

        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password = TestUser.Password });
        return (await JsonAsync(login)).GetProperty("token").GetString()!;
    }
}
