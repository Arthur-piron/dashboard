using System.Collections.Concurrent;
using System.Text.Json;
using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using DashboardBackEnd.Subscriptions;
using DashboardBackEnd.Widgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DashboardBackEnd.Timer;

public class WidgetTimer : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WidgetRegistry _registry;
    private readonly ILogger<WidgetTimer> _logger;
    private readonly TimeSpan _manualRefreshCooldown;

    private readonly ConcurrentDictionary<int, DateTime> _schedule = new();
    private readonly ConcurrentDictionary<int, WidgetResult> _results = new();
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();

    public WidgetTimer(
        IServiceScopeFactory scopes,
        WidgetRegistry registry,
        IOptions<TimerOptions> options,
        ILogger<WidgetTimer> logger)
    {
        _scopes = scopes;
        _registry = registry;
        _manualRefreshCooldown = TimeSpan.FromSeconds(options.Value.ManualRefreshCooldownSeconds);
        _logger = logger;
    }

    public void Register(WidgetInstance instance)
    {
        _schedule[instance.Id] = DateTime.UtcNow;
        _results[instance.Id] = Pending(instance);
    }

    public void Unregister(int instanceId)
    {
        _schedule.TryRemove(instanceId, out _);
        _results.TryRemove(instanceId, out _);
    }

    // a refresh asked by the user: fetch now and wait for it, unless one is already running
    // or the last fetch is too recent, in which case the cached record is the answer
    public async Task<WidgetResult?> RefreshNowAsync(int instanceId)
    {
        var cached = Find(instanceId);
        if (cached?.FetchedAt is { } fetchedAt && DateTime.UtcNow - fetchedAt < _manualRefreshCooldown)
        {
            return cached;
        }

        if (_inFlight.TryAdd(instanceId, 0))
        {
            await RefreshAsync(instanceId, CancellationToken.None);
        }

        return Find(instanceId);
    }

    public WidgetResult? Find(int instanceId) => _results.GetValueOrDefault(instanceId);

    public IReadOnlyList<WidgetResult> FindAll(IEnumerable<int> instanceIds) =>
        instanceIds.Select(Find).Where(r => r is not null).Select(r => r!).ToList();

    private static WidgetResult Pending(WidgetInstance instance) =>
        new(instance.Id, instance.WidgetKey, WidgetStatus.Pending, null, null, null, null);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await LoadFromDatabaseAsync(ct);

        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await tick.WaitForNextTickAsync(ct))
        {
            var now = DateTime.UtcNow;
            var due = _schedule.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();

            foreach (var id in due)
            {
                if (_inFlight.TryAdd(id, 0))
                {
                    _ = RefreshAsync(id, ct);
                }
            }
        }
    }

    private async Task LoadFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var instance in await db.WidgetInstances.AsNoTracking().ToListAsync(ct))
            {
                Register(instance);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load widget instances at startup.");
        }
    }

    private async Task RefreshAsync(int instanceId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

            var instance = await db.WidgetInstances.AsNoTracking()
                .SingleOrDefaultAsync(w => w.Id == instanceId, ct);

            if (instance is null)
            {
                Unregister(instanceId);
                return;
            }

            var result = await FetchAsync(instance, subscriptions, ct);
            _results[instanceId] = result;
            _schedule[instanceId] = DateTime.UtcNow.AddSeconds(instance.RefreshRateSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refreshing widget instance {InstanceId} failed unexpectedly.", instanceId);
            // Try again after a while rather than never.
            _schedule[instanceId] = DateTime.UtcNow.AddMinutes(1);
        }
        finally
        {
            _inFlight.TryRemove(instanceId, out _);
        }
    }

    private async Task<WidgetResult> FetchAsync(
        WidgetInstance instance, SubscriptionService subscriptions, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next = now.AddSeconds(instance.RefreshRateSeconds);
        var previous = Find(instance.Id);

        WidgetResult Failure(string message) =>
            new(instance.Id, instance.WidgetKey, WidgetStatus.Error, now, next, previous?.Data, message);

        var widget = _registry.FindWidget(instance.WidgetKey);
        if (widget is null)
        {
            return Failure($"This widget ('{instance.WidgetKey}') is no longer available.");
        }

        if (!await subscriptions.IsAvailableAsync(instance.UserId, widget.ServiceKey, ct))
        {
            return Failure($"Subscribe to '{widget.ServiceKey}' again to refresh this widget.");
        }

        try
        {
            var credentials = await subscriptions.GetCredentialsAsync(instance.UserId, widget.ServiceKey, ct);
            var config = JsonSerializer.Deserialize<JsonElement>(instance.ConfigJson);

            var data = await widget.FetchAsync(config, credentials, ct);
            return new WidgetResult(instance.Id, instance.WidgetKey, WidgetStatus.Ok, now, next, data, null);
        }
        catch (WidgetDataException ex)
        {
            return Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Widget {WidgetKey} (instance {InstanceId}) could not be refreshed.",
                instance.WidgetKey, instance.Id);
            return Failure("This widget could not be refreshed. It will be retried automatically.");
        }
    }
}
