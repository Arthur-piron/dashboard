using System.Text.Json;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Services.Nasa;

public class ApodWidget : IWidget
{
    private readonly NasaClient _nasa;

    public ApodWidget(NasaClient nasa)
    {
        _nasa = nasa;
    }

    public string Key => "nasa.apod";
    public string ServiceKey => NasaService.ServiceKey;
    public string Name => "Astronomy Picture of the Day";
    public string Description => "NASA's picture of the day, with its title and explanation.";

    public IReadOnlyList<WidgetParam> Parameters { get; } =
    [
        new("date", ParamType.String, Required: false,
            Description: "Day to show as YYYY-MM-DD, from 1995-06-16 onwards. Defaults to today."),
    ];

    public async Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct)
    {
        var apiKey = NasaService.ApiKeyOf(credentials);
        var date = config.TryGetProperty("date", out var d) ? d.GetString() : null;

        var picture = await _nasa.ApodAsync(apiKey, date, ct);

        return new
        {
            picture.Date,
            picture.Title,
            picture.Explanation,
            picture.MediaType,
            picture.Url,
            picture.HdUrl,
            picture.Copyright,
        };
    }
}
