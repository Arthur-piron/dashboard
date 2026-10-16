using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Weather;

public class WeatherService : IService
{
    public const string ServiceKey = "weather";

    public string Key => ServiceKey;
    public string Name => "Weather";
    public string Description => "Current conditions and forecasts for any city (Open-Meteo, no account needed).";
    public AuthKind Auth => AuthKind.None;
}
