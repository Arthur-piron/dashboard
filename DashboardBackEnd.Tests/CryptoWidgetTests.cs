using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DashboardBackEnd.Tests;

public class CryptoWidgetTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _api;
    private readonly MailpitClient _mailpit = new();

    public CryptoWidgetTests(ApiFactory factory)
    {
        _api = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var token = await TestUser.CreateAndLoginAsync(_api, _mailpit);
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> PreviewAsync(string widget, string query)
    {
        var response = await _api.GetAsync($"/widgets/{widget}/preview?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Crypto_needs_no_account()
    {
        var response = await _api.GetAsync("/services");
        var services = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var crypto = services.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "crypto");
        Assert.Equal("none", crypto.GetProperty("auth").GetString());
        Assert.True(crypto.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Price_returns_the_quote_with_its_daily_change()
    {
        var data = await PreviewAsync("crypto.price", $"coin={FakeCoinGeckoHandler.KnownCoin}");

        Assert.Equal(FakeCoinGeckoHandler.KnownCoin, data.GetProperty("coin").GetString());
        Assert.Equal("usd", data.GetProperty("currency").GetString());
        Assert.Equal(FakeCoinGeckoHandler.Price, data.GetProperty("price").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.ChangePercent24h, data.GetProperty("changePercent24h").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.MarketCap, data.GetProperty("marketCap").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.LastUpdatedAt, data.GetProperty("lastUpdatedAt").GetInt64());
    }

    [Fact]
    public async Task Price_accepts_a_coin_id_in_any_case()
    {
        var data = await PreviewAsync("crypto.price", "coin=BitCoin");

        Assert.Equal(FakeCoinGeckoHandler.KnownCoin, data.GetProperty("coin").GetString());
    }

    [Fact]
    public async Task Price_can_be_asked_in_another_currency()
    {
        var data = await PreviewAsync("crypto.price", $"coin={FakeCoinGeckoHandler.KnownCoin}&currency=eur");

        Assert.Equal("eur", data.GetProperty("currency").GetString());
        Assert.Equal(FakeCoinGeckoHandler.Price, data.GetProperty("price").GetDouble());
    }

    [Fact]
    public async Task Price_in_a_currency_that_is_not_offered_is_refused()
    {
        var response = await _api.GetAsync($"/widgets/crypto.price/preview?coin={FakeCoinGeckoHandler.KnownCoin}&currency=chf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be one of", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Price_without_a_coin_is_refused()
    {
        var response = await _api.GetAsync("/widgets/crypto.price/preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'coin' is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Price_of_an_unlisted_coin_returns_422()
    {
        var response = await _api.GetAsync("/widgets/crypto.price/preview?coin=not-a-coin");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("lists no coin", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rate_limited_provider_is_reported_as_data_the_user_can_retry()
    {
        var response = await _api.GetAsync($"/widgets/crypto.price/preview?coin={FakeCoinGeckoHandler.ThrottledCoin}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("rate limit", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task History_summarises_the_period()
    {
        var data = await PreviewAsync("crypto.history", $"coin={FakeCoinGeckoHandler.KnownCoin}&days=7");

        var last = FakeCoinGeckoHandler.PointCount(7) - 1;
        Assert.Equal(7, data.GetProperty("days").GetInt32());
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(0), data.GetProperty("first").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(last), data.GetProperty("last").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(0), data.GetProperty("low").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(last), data.GetProperty("high").GetDouble());
        Assert.True(data.GetProperty("changePercent").GetDouble() > 0);
    }

    [Fact]
    public async Task History_is_thinned_to_a_drawable_number_of_points()
    {
        var data = await PreviewAsync("crypto.history", $"coin={FakeCoinGeckoHandler.KnownCoin}&days=7");

        var points = data.GetProperty("points").EnumerateArray().ToList();

        Assert.True(FakeCoinGeckoHandler.PointCount(7) > 24, "the provider must return more points than the widget keeps");
        Assert.Equal(24, points.Count);

        // The ends of the series survive the thinning, and the points stay in order.
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(0), points[0].GetProperty("price").GetDouble());
        Assert.Equal(FakeCoinGeckoHandler.PriceAt(FakeCoinGeckoHandler.PointCount(7) - 1),
            points[^1].GetProperty("price").GetDouble());
        Assert.Equal(points.Select(p => p.GetProperty("timestampMs").GetInt64()).Order(),
            points.Select(p => p.GetProperty("timestampMs").GetInt64()));
    }

    [Fact]
    public async Task History_keeps_a_short_series_as_it_is()
    {
        var data = await PreviewAsync("crypto.history", $"coin={FakeCoinGeckoHandler.KnownCoin}&days=1");

        Assert.Equal(FakeCoinGeckoHandler.PointCount(1), data.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task History_beyond_the_allowed_period_is_refused()
    {
        var response = await _api.GetAsync($"/widgets/crypto.history/preview?coin={FakeCoinGeckoHandler.KnownCoin}&days=365");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be <= 90", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task History_of_an_unlisted_coin_returns_422()
    {
        var response = await _api.GetAsync("/widgets/crypto.history/preview?coin=not-a-coin");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("no price history", await response.Content.ReadAsStringAsync());
    }
}
