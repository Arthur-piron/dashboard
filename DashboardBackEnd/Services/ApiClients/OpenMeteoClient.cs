using System.Text.Json;

namespace DashboardBackEnd.Services.ApiClients;

public class OpenMeteoClient
{
    public const string HttpClientName = "open-meteo";

    private readonly IHttpClientFactory _http;

    public OpenMeteoClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public record Location(string Name, string Country, double Latitude, double Longitude);

    public async Task<Location?> GeocodeAsync(string city, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";

        using var doc = await client.GetFromJsonDocumentAsync(url, ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
        {
            return null;
        }

        var first = results[0];
        return new Location(
            first.GetProperty("name").GetString()!,
            first.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
            first.GetProperty("latitude").GetDouble(),
            first.GetProperty("longitude").GetDouble());
    }

    public async Task<JsonDocument> ForecastAsync(Location location, string query, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude}&longitude={location.Longitude}&timezone=auto&{query}";
        return await client.GetFromJsonDocumentAsync(url, ct);
    }

    public static string DescribeCode(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown",
    };
}
