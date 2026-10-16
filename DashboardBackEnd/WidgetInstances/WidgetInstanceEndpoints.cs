using System.Security.Claims;
using System.Text.Json;
using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using DashboardBackEnd.Subscriptions;
using DashboardBackEnd.Timer;
using DashboardBackEnd.Widgets;
using Microsoft.EntityFrameworkCore;

namespace DashboardBackEnd.Instances;

public static class WidgetInstanceEndpoints
{
    public const int MinRefreshRateSeconds = 10;
    public const int MaxRefreshRateSeconds = 24 * 60 * 60;

    public const int MaxGridSpan = 12;

    public static IEndpointRouteBuilder MapWidgetInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/widget-instances").WithTags("Widget instances").RequireAuthorization();

        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapGet("/data", AllData);
        group.MapGet("/{id:int}", Get);
        group.MapGet("/{id:int}/data", Data);
        group.MapPost("/{id:int}/refresh", Refresh);
        group.MapPut("/{id:int}", Update);
        group.MapPatch("/{id:int}/position", Move);
        group.MapDelete("/{id:int}", Delete);

        return app;
    }

    private static async Task<IReadOnlyList<WidgetInstanceDto>> List(
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetRegistry registry,
        CancellationToken ct)
    {
        var instances = await db.WidgetInstances
            .Where(w => w.UserId == principal.UserId())
            .OrderBy(w => w.Id)
            .ToListAsync(ct);

        return instances.Select(w => ToDto(w, registry)).ToList();
    }

    private static async Task<IResult> Get(
        int id,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetRegistry registry,
        CancellationToken ct)
    {
        var instance = await FindOwnedAsync(db, principal, id, ct);
        return instance is null ? Results.NotFound() : Results.Ok(ToDto(instance, registry));
    }

    private static async Task<IResult> Create(
        CreateWidgetInstanceRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetRegistry registry,
        SubscriptionService subscriptions,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var userId = principal.UserId();

        var widget = registry.FindWidget(request.WidgetKey);
        if (widget is null)
        {
            return Problem("widgetKey", $"Unknown widget '{request.WidgetKey}'.");
        }

        if (!await subscriptions.IsAvailableAsync(userId, widget.ServiceKey, ct))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not subscribed",
                detail: $"Subscribe to '{widget.ServiceKey}' before adding its widgets.");
        }

        var errors = Validate(widget, request.Config, request.RefreshRateSeconds, request.Position);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var position = request.Position ?? new PositionDto(0, 0, 1, 1);
        var instance = new WidgetInstance
        {
            UserId = userId,
            WidgetKey = widget.Key,
            ConfigJson = request.Config.GetRawText(),
            RefreshRateSeconds = request.RefreshRateSeconds,
            X = position.X,
            Y = position.Y,
            Width = position.Width,
            Height = position.Height,
        };

        db.WidgetInstances.Add(instance);
        await db.SaveChangesAsync(ct);

        timer.Register(instance);

        return Results.Created($"/widget-instances/{instance.Id}", ToDto(instance, registry));
    }

    private static async Task<IResult> Update(
        int id,
        UpdateWidgetInstanceRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetRegistry registry,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var instance = await FindOwnedAsync(db, principal, id, ct);
        if (instance is null) return Results.NotFound();

        var widget = registry.FindWidget(instance.WidgetKey)!;

        var errors = Validate(widget, request.Config, request.RefreshRateSeconds, position: null);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        instance.ConfigJson = request.Config.GetRawText();
        instance.RefreshRateSeconds = request.RefreshRateSeconds;
        instance.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        timer.Register(instance);

        return Results.Ok(ToDto(instance, registry));
    }

    private static async Task<IResult> Move(
        int id,
        PositionDto position,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetRegistry registry,
        CancellationToken ct)
    {
        var instance = await FindOwnedAsync(db, principal, id, ct);
        if (instance is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        AddPositionErrors(errors, position);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        instance.X = position.X;
        instance.Y = position.Y;
        instance.Width = position.Width;
        instance.Height = position.Height;
        instance.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(instance, registry));
    }

    private static async Task<IResult> Delete(
        int id,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var deleted = await db.WidgetInstances
            .Where(w => w.Id == id && w.UserId == principal.UserId())
            .ExecuteDeleteAsync(ct);

        if (deleted == 0) return Results.NotFound();

        timer.Unregister(id);
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<WidgetResult>> AllData(
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var ids = await db.WidgetInstances
            .Where(w => w.UserId == principal.UserId())
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(ct);

        return timer.FindAll(ids);
    }

    private static async Task<IResult> Data(
        int id,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var instance = await FindOwnedAsync(db, principal, id, ct);
        if (instance is null) return Results.NotFound();

        var result = timer.Find(id)
            ?? new WidgetResult(instance.Id, instance.WidgetKey, WidgetStatus.Pending, null, null, null, null);

        return Results.Ok(result);
    }

    private static async Task<IResult> Refresh(
        int id,
        ClaimsPrincipal principal,
        AppDbContext db,
        WidgetTimer timer,
        CancellationToken ct)
    {
        var instance = await FindOwnedAsync(db, principal, id, ct);
        if (instance is null) return Results.NotFound();

        var result = await timer.RefreshNowAsync(id)
            ?? new WidgetResult(instance.Id, instance.WidgetKey, WidgetStatus.Pending, null, null, null, null);

        return Results.Ok(result);
    }

    private static Task<WidgetInstance?> FindOwnedAsync(
        AppDbContext db, ClaimsPrincipal principal, int id, CancellationToken ct) =>
        db.WidgetInstances.SingleOrDefaultAsync(w => w.Id == id && w.UserId == principal.UserId(), ct);

    private static Dictionary<string, string[]> Validate(
        IWidget widget, JsonElement config, int refreshRateSeconds, PositionDto? position)
    {
        var errors = new Dictionary<string, string[]>();

        var configErrors = WidgetConfigValidator.Validate(widget, config);
        if (configErrors.Count > 0)
        {
            errors["config"] = configErrors.ToArray();
        }

        if (refreshRateSeconds is < MinRefreshRateSeconds or > MaxRefreshRateSeconds)
        {
            errors["refreshRateSeconds"] =
                [$"Must be between {MinRefreshRateSeconds} and {MaxRefreshRateSeconds} seconds."];
        }

        if (position is not null)
        {
            AddPositionErrors(errors, position);
        }

        return errors;
    }

    private static void AddPositionErrors(Dictionary<string, string[]> errors, PositionDto position)
    {
        var messages = new List<string>();

        if (position.X < 0 || position.Y < 0) messages.Add("X and Y must not be negative.");
        if (position.Width is < 1 or > MaxGridSpan || position.Height is < 1 or > MaxGridSpan)
        {
            messages.Add($"Width and height must be between 1 and {MaxGridSpan}.");
        }

        if (messages.Count > 0) errors["position"] = messages.ToArray();
    }

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static WidgetInstanceDto ToDto(WidgetInstance instance, WidgetRegistry registry) =>
        new(instance.Id,
            instance.WidgetKey,
            registry.FindWidget(instance.WidgetKey)?.ServiceKey ?? "",
            JsonSerializer.Deserialize<JsonElement>(instance.ConfigJson),
            instance.RefreshRateSeconds,
            new PositionDto(instance.X, instance.Y, instance.Width, instance.Height),
            instance.CreatedAt,
            instance.UpdatedAt);
}
