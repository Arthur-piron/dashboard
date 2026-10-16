using System.Security.Claims;
using System.Text.Json;
using DashboardBackEnd.Subscriptions;

namespace DashboardBackEnd.Widgets;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/about.json", About).WithTags("About");

        var group = app.MapGroup("").WithTags("Catalog").RequireAuthorization();
        group.MapGet("/services", Services);
        group.MapGet("/widgets", Widgets);
        group.MapGet("/widgets/{key}/preview", Preview);

        return app;
    }

    private static AboutResponse About(HttpContext http, WidgetRegistry registry)
    {
        var host = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var services = registry.Services
            .OrderBy(s => s.Key)
            .Select(s => new AboutService(
                s.Key,
                registry.WidgetsOf(s.Key)
                    .OrderBy(w => w.Key)
                    .Select(w => new AboutWidget(
                        w.Key,
                        w.Description,
                        w.Parameters.Select(p => new AboutParam(p.Name, ParamTypeName(p.Type))).ToList()))
                    .ToList()))
            .ToList();

        return new AboutResponse(
            new AboutClient(host),
            new AboutServer(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), services));
    }

    private static string ParamTypeName(ParamType type) => type switch
    {
        ParamType.Integer => "integer",
        ParamType.String => "string",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static async Task<IReadOnlyList<ServiceDto>> Services(
        ClaimsPrincipal principal,
        WidgetRegistry registry,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        var available = await subscriptions.AvailableServiceKeysAsync(principal.UserId(), ct);

        return registry.Services
            .OrderBy(s => s.Key)
            .Select(s => new ServiceDto(
                s.Key, s.Name, s.Description, s.Auth, available.Contains(s.Key),
                s.Auth == AuthKind.ApiKey ? s.CredentialPrompt : null))
            .ToList();
    }

    private static async Task<IReadOnlyList<WidgetDto>> Widgets(
        ClaimsPrincipal principal,
        WidgetRegistry registry,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        var available = await subscriptions.AvailableServiceKeysAsync(principal.UserId(), ct);

        return registry.Widgets
            .OrderBy(w => w.Key)
            .Select(w => new WidgetDto(
                w.Key, w.ServiceKey, w.Name, w.Description, w.Parameters,
                Available: available.Contains(w.ServiceKey)))
            .ToList();
    }

    private static async Task<IResult> Preview(
        string key,
        HttpContext http,
        ClaimsPrincipal principal,
        WidgetRegistry registry,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        var widget = registry.FindWidget(key);
        if (widget is null) return Results.NotFound();

        var userId = principal.UserId();
        if (!await subscriptions.IsAvailableAsync(userId, widget.ServiceKey, ct))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not subscribed",
                detail: $"Subscribe to '{widget.ServiceKey}' before using its widgets.");
        }

        var credentials = await subscriptions.GetCredentialsAsync(userId, widget.ServiceKey, ct);

        var config = QueryToConfig(http.Request.Query, widget);
        var errors = WidgetConfigValidator.Validate(widget, config);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["config"] = errors.ToArray() });
        }

        try
        {
            return Results.Ok(await widget.FetchAsync(config, credentials, ct));
        }
        catch (WidgetDataException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, detail: ex.Message);
        }
    }

    private static JsonElement QueryToConfig(IQueryCollection query, IWidget widget)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (name, values) in query)
        {
            var raw = values.ToString();
            var param = widget.Parameters.FirstOrDefault(p => p.Name == name);
            dict[name] = param?.Type == ParamType.Integer && int.TryParse(raw, out var n) ? n : raw;
        }
        return JsonSerializer.SerializeToElement(dict);
    }
}
