using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Weather;

public class CurrentWeatherWidget : IWidget
{
    private readonly OpenMeteoClient _meteo;

    public CurrentWeatherWidget(OpenMeteoClient meteo)
    {
        _meteo = meteo;
    }

    public string Key => "weather.current";
    public string ServiceKey => WeatherService.ServiceKey;
    public string Name => "Current weather";
    public string Description => "Temperature, humidity, wind and sky overview for a city right now.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("city", ParamType.String, Description: "City name, e.g. Paris"),
        new("unit", ParamType.String, Required: false, Description: "Temperature unit",
            Options: ["celsius", "fahrenheit"]),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var city = config.GetProperty("city").GetString()!;
        var unit = config.TryGetProperty("unit", out var u) ? u.GetString() ?? "celsius" : "celsius";

        var location = await _meteo.GeocodeAsync(city, ct)
            ?? throw new WidgetDataException($"Unknown city '{city}'.");

        using var doc = await _meteo.ForecastAsync(location,
            $"current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&temperature_unit={unit}", ct);

        var current = doc.RootElement.GetProperty("current");
        var code = current.GetProperty("weather_code").GetInt32();

        return new
        {
            City = location.Name,
            Country = location.Country,
            Temperature = current.GetProperty("temperature_2m").GetDouble(),
            Unit = unit,
            Humidity = current.GetProperty("relative_humidity_2m").GetInt32(),
            WindSpeedKmh = current.GetProperty("wind_speed_10m").GetDouble(),
            WeatherCode = code,
            Sky = OpenMeteoClient.DescribeCode(code),
            ObservedAt = current.GetProperty("time").GetString(),
        };
    }
}
