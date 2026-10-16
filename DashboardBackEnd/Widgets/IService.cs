namespace DashboardBackEnd.Widgets;

public interface IService
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    AuthKind Auth { get; }

    CredentialPrompt? CredentialPrompt => null;

    Task<bool> ValidateCredentialsAsync(ServiceCredentials credentials, CancellationToken ct) =>
        Task.FromResult(true);
}
