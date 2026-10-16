using System.Text.Json;

namespace DashboardBackEnd.Widgets;

public interface IWidget
{
    string Key { get; }
    string ServiceKey { get; }
    string Name { get; }
    string Description { get; }
    IReadOnlyList<WidgetParam> Parameters { get; }
    Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct);
}
