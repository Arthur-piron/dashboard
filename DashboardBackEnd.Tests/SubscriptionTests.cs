using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DashboardBackEnd.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardBackEnd.Tests;

public class SubscriptionTests : IClassFixture<ApiFactoryWithStubs>, IAsyncLifetime
{
    private readonly ApiFactoryWithStubs _factory;
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public SubscriptionTests(ApiFactoryWithStubs factory)
    {
        _factory = factory;
        _api = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> NewUserClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await TestUser.CreateAndLoginAsync(client, _mailpit);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<HttpResponseMessage> SubscribeAsync(string serviceKey, string apiKey, HttpClient? client = null) =>
        (client ?? _api).PostAsJsonAsync($"/subscriptions/{serviceKey}", new { apiKey });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<bool> IsSubscribedAsync(string serviceKey, HttpClient? client = null)
    {
        var services = await JsonAsync(await (client ?? _api).GetAsync("/services"));
        return services.EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == serviceKey)
            .GetProperty("subscribed").GetBoolean();
    }

    [Fact]
    public async Task An_api_key_service_tells_the_front_end_what_to_ask_for()
    {
        var services = await JsonAsync(await _api.GetAsync("/services"));

        var stub = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "stub-apikey");
        var prompt = stub.GetProperty("credentialPrompt");

        Assert.Equal("API key", prompt.GetProperty("label").GetString());
        Assert.Equal(StubApiKeyService.DemoKey, prompt.GetProperty("suggestedValue").GetString());
        Assert.Equal("https://example.test/get-a-key", prompt.GetProperty("signupUrl").GetString());
    }

    [Theory]
    [InlineData("weather")]     // needs no account
    [InlineData("stub-oauth")]  // linked through OAuth
    public async Task Services_that_need_no_pasted_key_expose_no_prompt(string serviceKey)
    {
        var services = await JsonAsync(await _api.GetAsync("/services"));

        var service = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == serviceKey);
        Assert.Equal(JsonValueKind.Null, service.GetProperty("credentialPrompt").ValueKind);
    }

    [Fact]
    public async Task The_suggested_value_works_as_a_credential()
    {
        var response = await SubscribeAsync("stub-apikey", StubApiKeyService.DemoKey);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsSubscribedAsync("stub-apikey"));
    }

    [Fact]
    public async Task Subscriptions_require_a_token()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_new_user_has_no_subscription()
    {
        var response = await _api.GetAsync("/subscriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await JsonAsync(response)).EnumerateArray());
    }

    [Fact]
    public async Task Subscribing_with_a_valid_key_makes_the_service_available()
    {
        Assert.False(await IsSubscribedAsync("stub-apikey"));

        var response = await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsSubscribedAsync("stub-apikey"));

        var list = await JsonAsync(await _api.GetAsync("/subscriptions"));
        var row = list.EnumerateArray().Single();
        Assert.Equal("stub-apikey", row.GetProperty("serviceKey").GetString());
    }

    [Fact]
    public async Task Subscribing_with_an_invalid_key_is_refused_and_stores_nothing()
    {
        var response = await SubscribeAsync("stub-apikey", "wrong-key");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(await IsSubscribedAsync("stub-apikey"));
    }

    [Fact]
    public async Task Subscribing_to_an_unknown_service_returns_404()
    {
        var response = await SubscribeAsync("nope", "anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("weather")]     // needs no account
    [InlineData("stub-oauth")]  // linked through OAuth, not a pasted key
    public async Task Subscribing_with_a_key_to_a_service_that_does_not_use_one_returns_400(string serviceKey)
    {
        var response = await SubscribeAsync(serviceKey, "anything");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subscribing_twice_replaces_the_credential()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        var list = await JsonAsync(await _api.GetAsync("/subscriptions"));
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task Unsubscribing_removes_the_access_and_is_idempotent()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        var first = await _api.DeleteAsync("/subscriptions/stub-apikey");
        var second = await _api.DeleteAsync("/subscriptions/stub-apikey");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.False(await IsSubscribedAsync("stub-apikey"));
    }

    [Fact]
    public async Task The_credential_never_leaves_the_server()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        foreach (var url in new[] { "/subscriptions", "/services", "/widgets" })
        {
            var body = await (await _api.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(StubApiKeyService.ValidKey, body);
        }
    }

    [Fact]
    public async Task The_credential_is_stored_encrypted()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ServiceSubscriptions
            .Where(s => s.ServiceKey == "stub-apikey")
            .Select(s => s.ProtectedCredentials)
            .ToListAsync();

        Assert.NotEmpty(stored);
        Assert.All(stored, value =>
        {
            Assert.NotEqual(StubApiKeyService.ValidKey, value);
            Assert.DoesNotContain(StubApiKeyService.ValidKey, value);
        });
    }

    [Fact]
    public async Task Widgets_of_an_unsubscribed_service_are_refused()
    {
        var response = await _api.GetAsync("/widgets/stub-apikey.echo/preview?label=hello");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_subscribed_widget_receives_the_decrypted_credential()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);

        var response = await _api.GetAsync("/widgets/stub-apikey.echo/preview?label=hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await JsonAsync(response);
        Assert.Equal("hello", data.GetProperty("label").GetString());
        Assert.Equal(StubApiKeyService.ValidKey, data.GetProperty("receivedApiKey").GetString());
    }

    [Fact]
    public async Task Subscriptions_are_isolated_between_users()
    {
        await SubscribeAsync("stub-apikey", StubApiKeyService.ValidKey);
        var other = await NewUserClientAsync();

        Assert.False(await IsSubscribedAsync("stub-apikey", other));
        Assert.Empty((await JsonAsync(await other.GetAsync("/subscriptions"))).EnumerateArray());

        // Deleting on the other account must not touch the first one's subscription.
        await other.DeleteAsync("/subscriptions/stub-apikey");
        Assert.True(await IsSubscribedAsync("stub-apikey"));
    }
}
