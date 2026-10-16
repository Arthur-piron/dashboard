using System.Text.Json;

namespace DashboardBackEnd.Services.ApiClients;

internal static class HttpClientJsonExtensions
{
    public static async Task<JsonDocument> GetFromJsonDocumentAsync(
        this HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
