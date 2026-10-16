using DashboardBackEnd.Auth.Oidc;
using DashboardBackEnd.Services.ApiClients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;


namespace DashboardBackEnd.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestDatabase = "dashboard_test";

    public const string GitHubClientId = "test-client-id";
    public const string GitHubClientSecret = "test-client-secret";
    public const string GitHubCallbackUrl = "http://localhost:5119/services/github/oauth/callback";

    public const string GoogleClientId = "test-google-client";
    public const string GoogleClientSecret = "test-google-secret";
    public const string MicrosoftClientId = "test-microsoft-client";
    public const string MicrosoftClientSecret = "test-microsoft-secret";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var current = context.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

            var testConnection = WithDatabase(current, TestDatabase);
            ForgetPreviousRunsOnce(testConnection);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = testConnection,

                // Credentials of an OAuth app that only FakeGitHubHandler ever answers.
                ["GitHub:ClientId"] = GitHubClientId,
                ["GitHub:ClientSecret"] = GitHubClientSecret,
                ["GitHub:CallbackUrl"] = GitHubCallbackUrl,

                // OpenID Connect apps that only FakeOidcHandler ever answers.
                ["Oidc:Google:ClientId"] = GoogleClientId,
                ["Oidc:Google:ClientSecret"] = GoogleClientSecret,
                ["Oidc:Microsoft:ClientId"] = MicrosoftClientId,
                ["Oidc:Microsoft:ClientSecret"] = MicrosoftClientSecret,
                ["Oidc:Microsoft:Tenant"] = "common",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(OpenMeteoClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeOpenMeteoHandler());

            services.AddHttpClient(DuolingoClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeDuolingoHandler());

            services.AddHttpClient(NasaClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeNasaHandler());

            services.AddHttpClient(CoinGeckoClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeCoinGeckoHandler());

            services.AddHttpClient(GitHubClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeGitHubHandler());

            services.AddHttpClient(OidcClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeOidcHandler());
        });
    }

    // Every host shares this one pool, so the whole suite stays well under Postgres's 100 connections.
    private const int MaxPoolSize = 40;

    private static string WithDatabase(string connectionString, string database)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("Maximum Pool Size=", StringComparison.OrdinalIgnoreCase))
            .Append($"Database={database}")
            .Append($"Maximum Pool Size={MaxPoolSize}");
        return string.Join(';', parts);
    }

    private static readonly object ForgetLock = new();
    private static bool _forgotten;

    // The test database persists, and every host's timer refreshes every instance it finds at boot:
    // left alone, earlier runs pile up hundreds of instances that each new host refreshes at once.
    // This runs before the first host of the run starts, so no test can lose an instance it created.
    private static void ForgetPreviousRunsOnce(string connectionString)
    {
        lock (ForgetLock)
        {
            if (_forgotten) return;
            _forgotten = true;

            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();
                using var command = new NpgsqlCommand("DELETE FROM \"WidgetInstances\"", connection);
                command.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.InvalidCatalogName
                or PostgresErrorCodes.UndefinedTable)
            {
                // a blank database: the first host's migration creates everything
            }
        }
    }
}
