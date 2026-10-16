namespace DashboardBackEnd.Widgets;

public record CredentialPrompt(
    string Label,
    string? Help = null,
    string? SuggestedValue = null,
    string? SignupUrl = null);
