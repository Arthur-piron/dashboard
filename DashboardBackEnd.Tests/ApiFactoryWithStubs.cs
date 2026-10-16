using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using DashboardBackEnd.Widgets;

namespace DashboardBackEnd.Tests;

// Adds stub services to the catalogue. WidgetRegistry receives IEnumerable<IService>/<IWidget>
// from DI, so anything registered here joins the catalogue for these tests only.
public class ApiFactoryWithStubs : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IService, StubApiKeyService>();
            services.AddSingleton<IService, StubOauthService>();
            services.AddSingleton<IWidget, StubApiKeyWidget>();
        });
    }
}
