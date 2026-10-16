using System.Security.Claims;
using DashboardBackEnd.Widgets;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DashboardBackEnd.Subscriptions;

public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/subscriptions").WithTags("Subscriptions").RequireAuthorization();

        group.MapGet("", List);
        group.MapPost("/{serviceKey}", Subscribe);
        group.MapDelete("/{serviceKey}", Unsubscribe);

        return app;
    }

    public static string UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

    private static async Task<IReadOnlyList<SubscriptionDto>> List(
        ClaimsPrincipal principal,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        var rows = await subscriptions.ListAsync(principal.UserId(), ct);
        return rows.Select(s => new SubscriptionDto(s.ServiceKey, s.CreatedAt, s.ExpiresAt)).ToList();
    }

    private static async Task<IResult> Subscribe(
        string serviceKey,
        SubscribeRequest request,
        ClaimsPrincipal principal,
        WidgetRegistry registry,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        var service = registry.FindService(serviceKey);
        if (service is null)
        {
            return Results.NotFound($"Unknown service '{serviceKey}'.");
        }

        if (service.Auth != AuthKind.ApiKey)
        {
            return Results.BadRequest(service.Auth == AuthKind.None
                ? $"Service '{serviceKey}' needs no subscription."
                : $"Service '{serviceKey}' is linked through OAuth, not an API key.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["apiKey"] = ["An API key is required."],
            });
        }

        var credentials = new ServiceCredentials(ApiKey: request.ApiKey);

        bool valid;
        try
        {
            valid = await service.ValidateCredentialsAsync(credentials, ct);
        }
        catch (WidgetDataException ex)
        {
            // the provider failed to answer, which says nothing about the key
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Provider unavailable",
                detail: ex.Message);
        }

        if (!valid)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Invalid credentials",
                detail: $"'{serviceKey}' rejected this API key.");
        }

        await subscriptions.SubscribeAsync(principal.UserId(), serviceKey, credentials, ct: ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Unsubscribe(
        string serviceKey,
        ClaimsPrincipal principal,
        SubscriptionService subscriptions,
        CancellationToken ct)
    {
        await subscriptions.UnsubscribeAsync(principal.UserId(), serviceKey, ct);
        return Results.NoContent();
    }
}
