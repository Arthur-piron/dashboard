using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Crypto;

public class PriceWidget : IWidget
{
    private readonly CoinGeckoClient _coinGecko;

    public PriceWidget(CoinGeckoClient coinGecko)
    {
        _coinGecko = coinGecko;
    }

    public string Key => "crypto.price";
    public string ServiceKey => CryptoService.ServiceKey;
    public string Name => "Coin price";
    public string Description => "Current price of a coin, with its 24-hour change and market cap.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("coin", ParamType.String, Description: "CoinGecko coin id, e.g. bitcoin or ethereum"),
        new("currency", ParamType.String, Required: false, Description: "Currency to price it in",
            Options: CoinGeckoClient.Currencies),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var coin = config.GetProperty("coin").GetString()!;
        var currency = config.TryGetProperty("currency", out var c) ? c.GetString() ?? "usd" : "usd";

        var price = await _coinGecko.PriceAsync(coin, currency, ct)
            ?? throw new WidgetDataException($"CoinGecko lists no coin '{coin}' priced in '{currency}'.");

        return new
        {
            price.Coin,
            price.Currency,
            price.Price,
            price.ChangePercent24h,
            price.MarketCap,
            price.LastUpdatedAt,
        };
    }
}
