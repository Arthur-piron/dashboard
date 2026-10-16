using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Crypto;

public class PriceHistoryWidget : IWidget
{
    // CoinGecko returns one point every few minutes for a short range, which is far more
    // than a widget can draw; the series is thinned to this many points before it is cached.
    private const int MaxPoints = 24;

    private readonly CoinGeckoClient _coinGecko;

    public PriceHistoryWidget(CoinGeckoClient coinGecko)
    {
        _coinGecko = coinGecko;
    }

    public string Key => "crypto.history";
    public string ServiceKey => CryptoService.ServiceKey;
    public string Name => "Price history";
    public string Description => "Price of a coin over the last days, with its low, high and change over the period.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("coin", ParamType.String, Description: "CoinGecko coin id, e.g. bitcoin or ethereum"),
        new("days", ParamType.Integer, Required: false, Description: "Length of the period in days (1-90)",
            Min: 1, Max: 90),
        new("currency", ParamType.String, Required: false, Description: "Currency to price it in",
            Options: CoinGeckoClient.Currencies),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var coin = config.GetProperty("coin").GetString()!;
        var days = config.TryGetProperty("days", out var d) ? d.GetInt32() : 7;
        var currency = config.TryGetProperty("currency", out var c) ? c.GetString() ?? "usd" : "usd";

        var series = await _coinGecko.HistoryAsync(coin, currency, days, ct);
        if (series is null or { Count: 0 })
        {
            throw new WidgetDataException($"CoinGecko has no price history for '{coin}' in '{currency}'.");
        }

        var points = Thin(series);
        var first = series[0].Price;
        var last = series[^1].Price;

        return new
        {
            Coin = coin.Trim().ToLowerInvariant(),
            Currency = currency.ToLowerInvariant(),
            Days = days,
            First = first,
            Last = last,
            Low = series.Min(p => p.Price),
            High = series.Max(p => p.Price),
            ChangePercent = first == 0 ? 0 : Math.Round((last - first) / first * 100, 2),
            Points = points.Select(p => new { p.TimestampMs, p.Price }),
        };
    }

    // Keeps the first and last point and spreads the rest evenly between them.
    private static IReadOnlyList<CoinGeckoClient.PricePoint> Thin(IReadOnlyList<CoinGeckoClient.PricePoint> series)
    {
        if (series.Count <= MaxPoints) return series;

        var step = (series.Count - 1) / (double)(MaxPoints - 1);
        return Enumerable.Range(0, MaxPoints)
            .Select(i => series[(int)Math.Round(i * step)])
            .ToList();
    }
}
