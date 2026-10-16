using System.Text;
using DashboardBackEnd.Admin;
using DashboardBackEnd.Auth;
using DashboardBackEnd.Auth.Oidc;
using DashboardBackEnd.Data;
using DashboardBackEnd.Instances;
using DashboardBackEnd.Email;
using DashboardBackEnd.Models;
using DashboardBackEnd.Services.ApiClients;
using DashboardBackEnd.Subscriptions;
using DashboardBackEnd.Timer;
using Microsoft.AspNetCore.DataProtection;
using DashboardBackEnd.Widgets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<EmailOptions>()
    .BindConfiguration(EmailOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<GitHubOptions>().BindConfiguration(GitHubOptions.SectionName);
builder.Services.AddOptions<AdminOptions>().BindConfiguration(AdminOptions.SectionName);
foreach (var provider in OidcProviders.All)
{
    builder.Services.AddOptions<OidcProviderOptions>(provider.Key)
        .BindConfiguration($"{OidcProviderOptions.SectionName}:{provider.ConfigSection}");
}
builder.Services.AddOptions<TimerOptions>()
    .BindConfiguration(TimerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = "role",
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AppRoles.AdminPolicy, policy => policy.RequireRole(AppRoles.Admin)));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<OidcClient>();
builder.Services.AddScoped<OidcSignIn>();
builder.Services.AddHttpClient(OidcClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<AdminSeeder>();
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

// Data Protection encrypts stored service credentials.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("DashboardBackEnd");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}
builder.Services.AddSingleton<CredentialProtector>();
builder.Services.AddScoped<SubscriptionService>();

builder.Services.AddWidgetCatalog();

builder.Services.AddSingleton<WidgetTimer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WidgetTimer>());
builder.Services.AddSingleton<DuolingoClient>();
builder.Services.AddHttpClient(DuolingoClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<OpenMeteoClient>();
builder.Services.AddHttpClient(OpenMeteoClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<NasaClient>();
builder.Services.AddHttpClient(NasaClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<CoinGeckoClient>();
builder.Services.AddHttpClient(CoinGeckoClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // CoinGecko answers 403 to requests without one.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DashboardBackEnd");
});

builder.Services.AddSingleton<GitHubClient>();
builder.Services.AddHttpClient(GitHubClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // GitHub rejects requests without one.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DashboardBackEnd");
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
        scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
}

app.MapGet("/", () => "DashboardBackEnd is running.");
app.MapAuthEndpoints();
app.MapOidcEndpoints();
app.MapCatalogEndpoints();
app.MapSubscriptionEndpoints();
app.MapGitHubOAuthEndpoints();
app.MapAdminEndpoints();
app.MapWidgetInstanceEndpoints();

app.Run();

public partial class Program { }
