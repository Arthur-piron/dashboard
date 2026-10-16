using System.Text.Json;

namespace DashboardBackEnd.Instances;

public record PositionDto(int X, int Y, int Width, int Height);

public record WidgetInstanceDto(
    int Id,
    string WidgetKey,
    string ServiceKey,
    JsonElement Config,
    int RefreshRateSeconds,
    PositionDto Position,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateWidgetInstanceRequest(
    string WidgetKey,
    JsonElement Config,
    int RefreshRateSeconds,
    PositionDto? Position = null);

// Reconfigure: the config and the refresh rate, not the position.
public record UpdateWidgetInstanceRequest(JsonElement Config, int RefreshRateSeconds);
