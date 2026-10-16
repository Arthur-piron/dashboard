namespace DashboardBackEnd.Subscriptions;

public record SubscriptionDto(string ServiceKey, DateTime CreatedAt, DateTime? ExpiresAt);

public record SubscribeRequest(string ApiKey);

public record GitHubAuthorizeResponse(string Url);
