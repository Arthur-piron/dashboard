using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

namespace DashboardBackEnd.Tests;

// Stands in for CoinGecko's public API so the suite never touches the network.
// Mirrors its two answers for an unknown coin: an empty object from simple/price, a 404 from market_chart.
public class FakeCoinGeckoHandler : HttpMessageHandler
{
    public const string KnownCoin = "bitcoin";
    public const string ThrottledCoin = "throttled-coin";

    public const double Price = 63000.5;
    public const double ChangePercent24h = 2.35;
    public const double MarketCap = 1_240_000_000_000;
    public const long LastUpdatedAt = 1790000000;

    // One point an hour, the granularity CoinGecko uses for a range of a few days.
    public const int PointsPerDay = 24;
    public const double FirstPrice = 100;
    public const double PriceStep = 10;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);

        if (uri.AbsolutePath.EndsWith("/simple/price"))
        {
            var ids = query["ids"] ?? "";
            var currency = query["vs_currencies"] ?? "usd";

            if (ids == ThrottledCoin) return Json(HttpStatusCode.TooManyRequests, """{"status":{"error_code":429}}""");

            if (ids != KnownCoin) return Json(HttpStatusCode.OK, "{}");

            return Json(HttpStatusCode.OK, """
            {"%COIN%":{"%VS%":%PRICE%,"%VS%_market_cap":%CAP%,
            "%VS%_24h_change":%CHANGE%,"last_updated_at":%UPDATED%}}
            """
                .Replace("%COIN%", KnownCoin)
                .Replace("%VS%", currency)
                .Replace("%PRICE%", Price.ToString(CultureInfo.InvariantCulture))
                .Replace("%CAP%", MarketCap.ToString(CultureInfo.InvariantCulture))
                .Replace("%CHANGE%", ChangePercent24h.ToString(CultureInfo.InvariantCulture))
                .Replace("%UPDATED%", LastUpdatedAt.ToString(CultureInfo.InvariantCulture)));
        }

        var coin = uri.AbsolutePath.Split('/')[4];
        if (coin == ThrottledCoin) return Json(HttpStatusCode.TooManyRequests, """{"status":{"error_code":429}}""");
        if (coin != KnownCoin) return Json(HttpStatusCode.NotFound, """{"error":"coin not found"}""");

        var days = int.TryParse(query["days"], out var d) ? d : 1;
        return Json(HttpStatusCode.OK, MarketChart(days));
    }

    public static int PointCount(int days) => days * PointsPerDay;

    public static double PriceAt(int index) => FirstPrice + index * PriceStep;

    private static string MarketChart(int days)
    {
        var points = string.Join(",", Enumerable.Range(0, PointCount(days))
            .Select(i => FormattableString.Invariant($"[{1790000000000L + i * 3_600_000L},{PriceAt(i)}]")));

        return $"{{\"prices\":[{points}],\"market_caps\":[],\"total_volumes\":[]}}";
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
}
