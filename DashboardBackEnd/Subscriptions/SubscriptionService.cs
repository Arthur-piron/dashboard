using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using DashboardBackEnd.Widgets;
using Microsoft.EntityFrameworkCore;

namespace DashboardBackEnd.Subscriptions;

public class SubscriptionService
{
    private readonly AppDbContext _db;
    private readonly CredentialProtector _protector;
    private readonly WidgetRegistry _registry;

    public SubscriptionService(AppDbContext db, CredentialProtector protector, WidgetRegistry registry)
    {
        _db = db;
        _protector = protector;
        _registry = registry;
    }

    public Task<List<ServiceSubscription>> ListAsync(string userId, CancellationToken ct = default) =>
        _db.ServiceSubscriptions.Where(s => s.UserId == userId).OrderBy(s => s.ServiceKey).ToListAsync(ct);

    public async Task<bool> IsAvailableAsync(string userId, string serviceKey, CancellationToken ct = default)
    {
        var service = _registry.FindService(serviceKey);
        if (service is null) return false;
        if (service.Auth == AuthKind.None) return true;

        return await _db.ServiceSubscriptions
            .AnyAsync(s => s.UserId == userId && s.ServiceKey == serviceKey, ct);
    }

    public async Task<HashSet<string>> AvailableServiceKeysAsync(string userId, CancellationToken ct = default)
    {
        var subscribed = await _db.ServiceSubscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.ServiceKey)
            .ToListAsync(ct);

        var keys = _registry.Services.Where(s => s.Auth == AuthKind.None).Select(s => s.Key).ToHashSet();
        keys.UnionWith(subscribed);
        return keys;
    }

    public async Task<ServiceCredentials?> GetCredentialsAsync(string userId, string serviceKey, CancellationToken ct = default)
    {
        var subscription = await _db.ServiceSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.ServiceKey == serviceKey, ct);

        return subscription is null ? null : _protector.Unprotect(subscription.ProtectedCredentials);
    }

    public async Task SubscribeAsync(
        string userId,
        string serviceKey,
        ServiceCredentials credentials,
        DateTime? expiresAt = null,
        CancellationToken ct = default)
    {
        var subscription = await _db.ServiceSubscriptions
            .SingleOrDefaultAsync(s => s.UserId == userId && s.ServiceKey == serviceKey, ct);

        if (subscription is null)
        {
            subscription = new ServiceSubscription { UserId = userId, ServiceKey = serviceKey };
            _db.ServiceSubscriptions.Add(subscription);
        }

        subscription.ProtectedCredentials = _protector.Protect(credentials);
        subscription.ExpiresAt = expiresAt;

        await _db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(string userId, string serviceKey, CancellationToken ct = default)
    {
        await _db.ServiceSubscriptions
            .Where(s => s.UserId == userId && s.ServiceKey == serviceKey)
            .ExecuteDeleteAsync(ct);
    }
}
