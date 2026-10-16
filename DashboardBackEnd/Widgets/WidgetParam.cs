namespace DashboardBackEnd.Widgets;

public enum ParamType
{
    Integer,
    String,
}

public record WidgetParam(
    string Name,
    ParamType Type,
    bool Required = true,
    string? Description = null,
    string[]? Options = null,
    int? Min = null,
    int? Max = null);
