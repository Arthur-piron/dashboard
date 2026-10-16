using System.Text.Json;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Tests;

// Registered only by ApiFactoryWithStubs, so the real catalogue (and /about.json) stays untouched.

// Stands for a service whose credential is pasted by the user.
public class StubApiKeyService : IService
{
    public const string ValidKey = "valid-api-key";

    // Public, like NASA's DEMO_KEY: safe to show in the UI and to pre-fill.
    public const string DemoKey = "demo-key";

    public string Key => "stub-apikey";
    public string Name => "Stub API key service";
    public string Description => "Test double for a service subscribed to with an API key.";
    public AuthKind Auth => AuthKind.ApiKey;

    public CredentialPrompt? CredentialPrompt => new(
        Label: "API key",
        Help: "Use the demo key to try it without signing up.",
        SuggestedValue: DemoKey,
        SignupUrl: "https://example.test/get-a-key");

    public Task<bool> ValidateCredentialsAsync(ServiceCredentials credentials, CancellationToken ct) =>
        Task.FromResult(credentials.ApiKey is ValidKey or DemoKey);
}

// Stands for a service linked through OAuth (GitHub, in #17).
public class StubOauthService : IService
{
    public string Key => "stub-oauth";
    public string Name => "Stub OAuth service";
    public string Description => "Test double for a service linked through OAuth.";
    public AuthKind Auth => AuthKind.Oauth;
}

// Echoes back whether a credential reached it, so tests can prove decryption works end to end.
public class StubApiKeyWidget : IWidget
{
    public string Key => "stub-apikey.echo";
    public string ServiceKey => "stub-apikey";
    public string Name => "Echo";
    public string Description => "Returns the credential the widget received.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("label", ParamType.String, Description: "Anything"),
    ];

    public Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct) =>
        Task.FromResult<object>(new
        {
            Label = config.GetProperty("label").GetString(),
            ReceivedApiKey = credentials?.ApiKey,
        });
}
