using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Crypto;

public class CryptoService : IService
{
    public const string ServiceKey = "crypto";

    public string Key => ServiceKey;
    public string Name => "Crypto";
    public string Description => "Prices and price history of any coin listed on CoinGecko (no account needed).";
    public AuthKind Auth => AuthKind.None;
}
