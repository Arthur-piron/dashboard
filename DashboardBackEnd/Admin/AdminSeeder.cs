using DashboardBackEnd.Auth;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DashboardBackEnd.Admin;

// The configured administrator may confirm their address before or after the process starts,
// so promotion happens both at startup and in /auth/confirm (and on a first OIDC sign-in).
public class AdminSeeder
{
    private readonly AdminOptions _options;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(IOptions<AdminOptions> options, ILogger<AdminSeeder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureRoleExistsAsync(RoleManager<IdentityRole> roles, CancellationToken ct = default)
    {
        if (!await roles.RoleExistsAsync(AppRoles.Admin))
        {
            await roles.CreateAsync(new IdentityRole(AppRoles.Admin));
        }
    }

    public async Task PromoteIfConfiguredAsync(AppUser user, UserManager<AppUser> users, CancellationToken ct = default)
    {
        // Owning the address is what grants the role: an unconfirmed account, or one created from a
        // provider that does not verify emails, may carry the configured address without owning it.
        if (!_options.Matches(user.Email) || !user.EmailConfirmed || await users.IsInRoleAsync(user, AppRoles.Admin))
        {
            return;
        }

        var result = await users.AddToRoleAsync(user, AppRoles.Admin);
        if (result.Succeeded)
        {
            _logger.LogInformation("Granted the {Role} role to the configured administrator.", AppRoles.Admin);
        }
        else
        {
            _logger.LogError("Could not grant the {Role} role: {Errors}",
                AppRoles.Admin, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task SeedAsync(RoleManager<IdentityRole> roles, UserManager<AppUser> users, CancellationToken ct = default)
    {
        await EnsureRoleExistsAsync(roles, ct);

        if (!_options.IsConfigured) return;

        var user = await users.FindByEmailAsync(_options.Email!);
        if (user is null)
        {
            _logger.LogInformation(
                "No account for the configured administrator yet; it will be promoted when it registers.");
            return;
        }

        await PromoteIfConfiguredAsync(user, users, ct);
    }
}
