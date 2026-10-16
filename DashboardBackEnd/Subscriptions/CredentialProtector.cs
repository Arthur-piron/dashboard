using System.Text.Json;
using DashboardBackEnd.Widgets;
using Microsoft.AspNetCore.DataProtection;

namespace DashboardBackEnd.Subscriptions;

public class CredentialProtector
{
    private readonly IDataProtector _protector;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("DashboardBackEnd.ServiceCredentials.v1");
    }

    public string Protect(ServiceCredentials credentials) =>
        _protector.Protect(JsonSerializer.Serialize(credentials));

    public ServiceCredentials? Unprotect(string protectedValue)
    {
        try
        {
            return JsonSerializer.Deserialize<ServiceCredentials>(_protector.Unprotect(protectedValue));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }
}
