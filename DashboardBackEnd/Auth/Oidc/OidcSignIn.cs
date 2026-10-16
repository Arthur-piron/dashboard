using System.Text;
using DashboardBackEnd.Admin;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DashboardBackEnd.Auth.Oidc;

public record OidcSignInResult(AppUser? User, string? Failure)
{
    public static OidcSignInResult Ok(AppUser user) => new(user, null);

    public static OidcSignInResult Fail(string reason) => new(null, reason);
}

// Turns a validated id_token into a Dashboard user: the one already linked to it, an existing
// account it is allowed to link to, or a new account.
public class OidcSignIn
{
    private readonly UserManager<AppUser> _users;
    private readonly AdminSeeder _admins;
    private readonly ILogger<OidcSignIn> _logger;

    public OidcSignIn(UserManager<AppUser> users, AdminSeeder admins, ILogger<OidcSignIn> logger)
    {
        _users = users;
        _admins = admins;
        _logger = logger;
    }

    public async Task<OidcSignInResult> ResolveAsync(OidcProvider provider, JsonWebToken token, CancellationToken ct)
    {
        var subject = token.Subject;
        if (string.IsNullOrEmpty(subject)) return OidcSignInResult.Fail("invalid_token");

        var linked = await _users.FindByLoginAsync(provider.LoginProvider, subject);
        if (linked is not null) return OidcSignInResult.Ok(linked);

        var email = provider.Email(token);
        if (string.IsNullOrWhiteSpace(email)) return OidcSignInResult.Fail("no_email");

        var verified = provider.EmailIsVerified(token);
        var login = new UserLoginInfo(provider.LoginProvider, subject, provider.DisplayName);

        var existing = await _users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Linking hands the account to whoever controls the provider identity, so both sides
            // must have proven the mailbox: an unconfirmed account may have been registered by
            // someone else with this address, waiting for the real owner to sign in.
            if (!verified) return OidcSignInResult.Fail("email_in_use");
            if (!existing.EmailConfirmed) return OidcSignInResult.Fail("email_not_confirmed");

            var added = await _users.AddLoginAsync(existing, login);
            return added.Succeeded ? OidcSignInResult.Ok(existing) : Failed("link", added);
        }

        var user = new AppUser
        {
            Email = email,
            UserName = await UniqueUserNameAsync(email),
            EmailConfirmed = verified,
        };

        var created = await _users.CreateAsync(user);
        if (!created.Succeeded) return Failed("create", created);

        var recorded = await _users.AddLoginAsync(user, login);
        if (!recorded.Succeeded)
        {
            await _users.DeleteAsync(user);
            return Failed("link", recorded);
        }

        await _admins.PromoteIfConfiguredAsync(user, _users, ct);
        return OidcSignInResult.Ok(user);
    }

    private OidcSignInResult Failed(string step, IdentityResult result)
    {
        _logger.LogError("OIDC sign-in could not {Step} the account: {Errors}",
            step, string.Join("; ", result.Errors.Select(e => e.Description)));
        return OidcSignInResult.Fail("account_failed");
    }

    // The local part of the address, kept to the characters Identity accepts, made unique.
    private async Task<string> UniqueUserNameAsync(string email)
    {
        var allowed = _users.Options.User.AllowedUserNameCharacters;
        var local = email.Split('@')[0];

        var name = new StringBuilder();
        foreach (var c in local)
        {
            if (allowed.Contains(c) && c != '@') name.Append(c);
        }

        var baseName = name.Length > 0 ? name.ToString() : "user";
        if (baseName.Length > 32) baseName = baseName[..32];

        var candidate = baseName;
        for (var suffix = 2; await _users.FindByNameAsync(candidate) is not null; suffix++)
        {
            candidate = suffix <= 50 ? $"{baseName}{suffix}" : $"{baseName}{Guid.NewGuid():N}"[..(baseName.Length + 8)];
        }

        return candidate;
    }
}
