using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Weather;

public class ForecastWidget : IWidget
{
    private readonly OpenMeteoClient _meteo;

    public ForecastWidget(OpenMeteoClient meteo)
    {
        _meteo = meteo;
    }

    public string Key => "weather.forecast";
    public string ServiceKey => WeatherService.ServiceKey;
    public string Name => "Forecast";
    public string Description => "Daily min/max temperature, precipitation and sky for the next days.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("city", ParamType.String, Description: "City name, e.g. Lille"),
        new("days", ParamType.Integer, Required: false, Description: "Number of days (1-7)", Min: 1, Max: 7),
        new("unit", ParamType.String, Required: false, Description: "Temperature unit",
            Options: ["celsius", "fahrenheit"]),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var city = config.GetProperty("city").GetString()!;
        var days = config.TryGetProperty("days", out var d) ? d.GetInt32() : 3;
        var unit = config.TryGetProperty("unit", out var u) ? u.GetString() ?? "celsius" : "celsius";

        var location = await _meteo.GeocodeAsync(city, ct)
            ?? throw new WidgetDataException($"Unknown city '{city}'.");

        using var doc = await _meteo.ForecastAsync(location,
            $"daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum&forecast_days={days}&temperature_unit={unit}", ct);

        var daily = doc.RootElement.GetProperty("daily");
        var dates = daily.GetProperty("time").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var codes = daily.GetProperty("weather_code").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var max = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var min = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var rain = daily.GetProperty("precipitation_sum").EnumerateArray().Select(e => e.GetDouble()).ToArray();

        return new
        {
            City = location.Name,
            Country = location.Country,
            Unit = unit,
            Days = dates.Select((date, i) => new
            {
                Date = date,
                Min = min[i],
                Max = max[i],
                PrecipitationMm = rain[i],
                WeatherCode = codes[i],
                Sky = OpenMeteoClient.DescribeCode(codes[i]),
            }),
        };
    }
}
