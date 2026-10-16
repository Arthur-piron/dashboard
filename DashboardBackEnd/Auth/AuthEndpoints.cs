using System.Security.Claims;
using System.Text;
using DashboardBackEnd.Admin;
using DashboardBackEnd.Email;
using DashboardBackEnd.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DashboardBackEnd.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapGet("/confirm", Confirm);
        group.MapPost("/login", Login);
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<AppUser> users,
        IEmailSender email,
        IConfiguration config,
        ILogger<AppUser> logger,
        CancellationToken ct)
    {
        var user = new AppUser { UserName = request.Username, Email = request.Email };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var frontendUrl = config["Frontend:Url"]?.TrimEnd('/') ?? "";
        var link = $"{frontendUrl}/confirm?userId={user.Id}&token={encodedToken}";

        try
        {
            await email.SendAsync(
                user.Email!,
                "Confirm your Dashboard account",
                $"""
                <p>Hi {user.UserName},</p>
                <p>Click the link below to confirm your account:</p>
                <p><a href="{link}">{link}</a></p>
                """,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send confirmation email to {Email}", user.Email);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> Confirm(
        string userId,
        string token,
        UserManager<AppUser> users,
        AdminSeeder admins,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.BadRequest("Invalid confirmation link.");
        }

        if (user.EmailConfirmed)
        {
            return Results.Ok("Email already confirmed.");
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid confirmation link.");
        }

        var result = await users.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            return Results.BadRequest("Invalid or expired confirmation link.");
        }

        // Promotion needs a proven address, and the startup seed misses one confirmed later.
        await admins.PromoteIfConfiguredAsync(user, users, ct);

        return Results.Ok("Email confirmed. You can now log in.");
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<AppUser> users,
        TokenService tokens)
    {
        var user = await users.FindByEmailAsync(request.Email);

        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            return Results.Unauthorized();
        }

        if (!user.EmailConfirmed)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email not confirmed",
                detail: "Check your inbox for the confirmation link.",
                type: "email_not_confirmed");
        }

        var roles = await users.GetRolesAsync(user);
        var (token, expiresAt) = tokens.CreateToken(user, roles);

        return Results.Ok(new LoginResponse(token, expiresAt));
    }

    // Reads the validated JWT only: no database access.
    private static IResult Me(ClaimsPrincipal principal)
    {
        return Results.Ok(new MeResponse(
            Id: principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!,
            Username: principal.Identity?.Name ?? "",
            Email: principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "",
            Roles: principal.FindAll("role").Select(c => c.Value).ToList()));
    }
}
