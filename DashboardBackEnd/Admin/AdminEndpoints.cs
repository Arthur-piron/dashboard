using System.Security.Claims;
using DashboardBackEnd.Auth;
using DashboardBackEnd.Data;
using DashboardBackEnd.Models;
using DashboardBackEnd.Subscriptions;
using DashboardBackEnd.Timer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DashboardBackEnd.Admin;

// A non-admin gets 403, not the 404 the per-user endpoints return: a role gate is not
// an ownership check, and an administrator may know which ids exist.
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/users").WithTags("Admin")
            .RequireAuthorization(AppRoles.AdminPolicy);

        group.MapGet("", List);
        group.MapDelete("/{id}", Delete);
        group.MapPost("/{id}/admin", Grant);
        group.MapDelete("/{id}/admin", Revoke);

        return app;
    }

    private static async Task<IReadOnlyList<AdminUserDto>> List(
        AppDbContext db,
        UserManager<AppUser> users,
        CancellationToken ct)
    {
        var accounts = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);

        var instanceCounts = await db.WidgetInstances
            .GroupBy(w => w.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

        var subscriptionCounts = await db.ServiceSubscriptions
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

        var roles = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var rolesByUser = roles
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name!).ToList());

        return accounts.Select(u => new AdminUserDto(
            Id: u.Id,
            Username: u.UserName ?? "",
            Email: u.Email ?? "",
            EmailConfirmed: u.EmailConfirmed,
            CreatedAt: u.CreatedAt,
            Roles: rolesByUser.GetValueOrDefault(u.Id, []),
            WidgetInstances: instanceCounts.GetValueOrDefault(u.Id),
            Subscriptions: subscriptionCounts.GetValueOrDefault(u.Id))).ToList();
    }

    private static async Task<IResult> Delete(
        string id,
        ClaimsPrincipal principal,
        AppDbContext db,
        UserManager<AppUser> users,
        WidgetTimer timer,
        CancellationToken ct)
    {
        if (id == principal.UserId())
        {
            return Refuse("An administrator cannot delete their own account.");
        }

        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();

        // The cascade removes the rows, but the timer keeps its own schedule.
        var instanceIds = await db.WidgetInstances
            .Where(w => w.UserId == id)
            .Select(w => w.Id)
            .ToListAsync(ct);

        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        foreach (var instanceId in instanceIds)
        {
            timer.Unregister(instanceId);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Grant(
        string id,
        UserManager<AppUser> users,
        RoleManager<IdentityRole> roles,
        AdminSeeder seeder,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();

        await seeder.EnsureRoleExistsAsync(roles, ct);

        if (await users.IsInRoleAsync(user, AppRoles.Admin))
        {
            return Results.NoContent();
        }

        var result = await users.AddToRoleAsync(user, AppRoles.Admin);
        return result.Succeeded
            ? Results.NoContent()
            : Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
    }

    private static async Task<IResult> Revoke(
        string id,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        CancellationToken ct)
    {
        // Blocking this is what guarantees one administrator always remains.
        if (id == principal.UserId())
        {
            return Refuse("An administrator cannot revoke their own role.");
        }

        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();

        if (!await users.IsInRoleAsync(user, AppRoles.Admin))
        {
            return Results.NoContent();
        }

        var result = await users.RemoveFromRoleAsync(user, AppRoles.Admin);
        return result.Succeeded
            ? Results.NoContent()
            : Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
    }

    private static IResult Refuse(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Refused",
        detail: detail);
}
