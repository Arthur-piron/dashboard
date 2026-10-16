namespace DashboardBackEnd.Timer;

public enum WidgetStatus
{
    Pending,
    Ok,
    Error,
}

public record WidgetResult(
    int InstanceId,
    string WidgetKey,
    WidgetStatus Status,
    DateTime? FetchedAt,
    DateTime? NextRefreshAt,
    object? Data,
    string? Error);
