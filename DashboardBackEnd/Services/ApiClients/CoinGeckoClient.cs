using System.Net;
using System.Text.Json;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.ApiClients;

public class CoinGeckoClient
{
    public const string HttpClientName = "coingecko";

    // Currencies the widgets offer; CoinGecko supports many more.
    public static readonly string[] Currencies = ["usd", "eur", "gbp", "jpy", "btc"];

    private readonly IHttpClientFactory _http;

    public CoinGeckoClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public record CoinPrice(
        string Coin,
        string Currency,
        double Price,
        double? ChangePercent24h,
        double? MarketCap,
        long? LastUpdatedAt);

    public record PricePoint(long TimestampMs, double Price);

    public async Task<CoinPrice?> PriceAsync(string coin, string currency, CancellationToken ct)
    {
        var id = coin.Trim().ToLowerInvariant();
        var vs = currency.ToLowerInvariant();

        var url = "https://api.coingecko.com/api/v3/simple/price"
            + $"?ids={Uri.EscapeDataString(id)}&vs_currencies={Uri.EscapeDataString(vs)}"
            + "&include_market_cap=true&include_24hr_change=true&include_last_updated_at=true";

        using var doc = await GetAsync(url, ct);

        // An unknown coin id is answered with an empty object, not an error.
        if (!doc.RootElement.TryGetProperty(id, out var entry)
            || !entry.TryGetProperty(vs, out var price)
            || price.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return new CoinPrice(
            Coin: id,
            Currency: vs,
            Price: price.GetDouble(),
            ChangePercent24h: GetDouble(entry, $"{vs}_24h_change"),
            MarketCap: GetDouble(entry, $"{vs}_market_cap"),
            LastUpdatedAt: GetDouble(entry, "last_updated_at") is { } seconds ? (long)seconds : null);
    }

    public async Task<IReadOnlyList<PricePoint>?> HistoryAsync(string coin, string currency, int days, CancellationToken ct)
    {
        var id = coin.Trim().ToLowerInvariant();
        var vs = currency.ToLowerInvariant();

        var url = $"https://api.coingecko.com/api/v3/coins/{Uri.EscapeDataString(id)}/market_chart"
            + $"?vs_currency={Uri.EscapeDataString(vs)}&days={days}";

        using var doc = await GetAsync(url, ct);

        if (!doc.RootElement.TryGetProperty("prices", out var prices) || prices.GetArrayLength() == 0)
        {
            return null;
        }

        return prices.EnumerateArray()
            .Where(p => p.GetArrayLength() >= 2)
            .Select(p => new PricePoint(p[0].GetInt64(), p[1].GetDouble()))
            .ToList();
    }

    // The public API has no key, so its rate limit is the failure users actually meet.
    // A 404 (unknown coin id on the history endpoint) is left for the caller to phrase.
    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return JsonDocument.Parse("{}");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new WidgetDataException("CoinGecko's public rate limit was reached. Try again in a minute.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static double? GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
