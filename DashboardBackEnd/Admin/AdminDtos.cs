namespace DashboardBackEnd.Admin;

public record AdminUserDto(
    string Id,
    string Username,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    int WidgetInstances,
    int Subscriptions);
