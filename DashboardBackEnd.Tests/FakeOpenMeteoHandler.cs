using System.Net;
using System.Text;
using System.Web;

namespace DashboardBackEnd.Tests;

// Stands in for Open-Meteo so the suite never depends on the network.
// Answers the two endpoints OpenMeteoClient calls, with fixed data.
public class FakeOpenMeteoHandler : HttpMessageHandler
{
    private static readonly Dictionary<string, (string Country, double Lat, double Lon)> Cities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lille"] = ("France", 50.63, 3.06),
        ["Tokyo"] = ("Japan", 35.68, 139.75),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);

        var json = uri.Host.StartsWith("geocoding")
            ? Geocode(query["name"]!)
            : Forecast(query);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private static string Geocode(string name)
    {
        if (!Cities.TryGetValue(name, out var city))
        {
            return """{"generationtime_ms":0.1}"""; // no "results" property: unknown city
        }

        return $$"""
        {"results":[{"id":1,"name":"{{name}}","country":"{{city.Country}}",
        "latitude":{{city.Lat}},"longitude":{{city.Lon}},"timezone":"Europe/Paris"}]}
        """;
    }

    private static string Forecast(System.Collections.Specialized.NameValueCollection query)
    {
        if (query["current"] is not null)
        {
            return """
            {"current":{"time":"2026-09-18T16:15","temperature_2m":19.3,
            "relative_humidity_2m":53,"weather_code":2,"wind_speed_10m":19.1}}
            """;
        }

        var days = int.TryParse(query["forecast_days"], out var d) ? d : 3;
        var dates = string.Join(",", Enumerable.Range(0, days).Select(i => $"\"2026-09-{18 + i:00}\""));
        var codes = string.Join(",", Enumerable.Repeat("3", days));
        var max = string.Join(",", Enumerable.Repeat("22.5", days));
        var min = string.Join(",", Enumerable.Repeat("11.0", days));
        var rain = string.Join(",", Enumerable.Repeat("0.4", days));

        return $$$"""
        {"daily":{"time":[{{{dates}}}],"weather_code":[{{{codes}}}],
        "temperature_2m_max":[{{{max}}}],"temperature_2m_min":[{{{min}}}],"precipitation_sum":[{{{rain}}}]}}
        """;
    }
}
