namespace DashboardBackEnd.Auth;

public record RegisterRequest(string Email, string Username, string Password);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt);
public record MeResponse(string Id, string Username, string Email, IReadOnlyList<string> Roles);
