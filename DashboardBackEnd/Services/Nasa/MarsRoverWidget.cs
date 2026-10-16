using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Nasa;

public class MarsRoverWidget : IWidget
{
    private readonly NasaClient _nasa;

    public MarsRoverWidget(NasaClient nasa)
    {
        _nasa = nasa;
    }

    public string Key => "nasa.mars";
    public string ServiceKey => NasaService.ServiceKey;
    public string Name => "Mars rover photos";
    public string Description => "The latest photos published by a Mars rover, with the camera that took them.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("rover", ParamType.String, Description: "Which rover to follow",
            Options: ["curiosity", "opportunity", "spirit"]),
        new("count", ParamType.Integer, Required: false,
            Description: "How many photos to return (1-10)", Min: 1, Max: 10),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var apiKey = NasaService.ApiKeyOf(credentials);
        var rover = config.GetProperty("rover").GetString()!;
        var count = config.TryGetProperty("count", out var c) ? c.GetInt32() : 3;

        var latest = await _nasa.LatestMarsPhotosAsync(apiKey, rover, count, ct);

        return new
        {
            latest.Rover,
            latest.Status,
            latest.EarthDate,
            latest.Sol,
            Photos = latest.Photos.Select(p => new { p.Id, p.Camera, p.ImageUrl }),
        };
    }
}
