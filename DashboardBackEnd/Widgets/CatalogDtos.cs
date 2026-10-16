namespace DashboardBackEnd.Widgets;

public record ServiceDto(
    string Key,
    string Name,
    string Description,
    AuthKind Auth,
    bool Subscribed,
    CredentialPrompt? CredentialPrompt);

public record WidgetDto(
    string Key,
    string ServiceKey,
    string Name,
    string Description,
    IReadOnlyList<WidgetParam> Params,
    bool Available);
