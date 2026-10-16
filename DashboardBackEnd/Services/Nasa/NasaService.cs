using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Nasa;

public class NasaService : IService
{
    public const string ServiceKey = "nasa";

    private readonly NasaClient _nasa;

    public NasaService(NasaClient nasa)
    {
        _nasa = nasa;
    }

    public string Key => ServiceKey;
    public string Name => "NASA";
    public string Description => "Astronomy Picture of the Day and Mars rover photos, with your own NASA API key.";
    public AuthKind Auth => AuthKind.ApiKey;

    public CredentialPrompt? CredentialPrompt => new(
        Label: "API key",
        Help: "Paste your own key from api.nasa.gov: it is free and arrives by email at once.",
        SignupUrl: "https://api.nasa.gov");

    public async Task<bool> ValidateCredentialsAsync(ServiceCredentials credentials, CancellationToken ct) =>
        credentials.ApiKey is { Length: > 0 } apiKey && await _nasa.IsValidKeyAsync(apiKey, ct);

    public static string ApiKeyOf(ServiceCredentials? credentials) =>
        credentials?.ApiKey is { Length: > 0 } apiKey
            ? apiKey
            : throw new WidgetDataException("Subscribe to NASA with an API key before using this widget.");
}
