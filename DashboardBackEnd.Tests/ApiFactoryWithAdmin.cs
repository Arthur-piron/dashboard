using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace DashboardBackEnd.Tests;

// The address is unique per instance: the test database persists between runs, so a
// fixed one would already be registered on the second run.
public class ApiFactoryWithAdmin : ApiFactory
{
    public string AdminEmail { get; } = $"admin-{Guid.NewGuid():N}@example.com";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Email"] = AdminEmail,
            }));
    }
}
