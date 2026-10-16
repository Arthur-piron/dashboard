using System.Text.Json.Serialization;

namespace DashboardBackEnd.Widgets;

public record AboutResponse(
    [property: JsonPropertyName("client")] AboutClient Client,
    [property: JsonPropertyName("server")] AboutServer Server);

public record AboutClient([property: JsonPropertyName("host")] string Host);

public record AboutServer(
    [property: JsonPropertyName("current_time")] long CurrentTime,
    [property: JsonPropertyName("services")] IReadOnlyList<AboutService> Services);

public record AboutService(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("widgets")] IReadOnlyList<AboutWidget> Widgets);

public record AboutWidget(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("params")] IReadOnlyList<AboutParam> Params);

public record AboutParam(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);
