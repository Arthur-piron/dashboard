# Back end

For whoever writes or reviews API code. The route-by-route reference is in [api.md](api.md); the
reasoning behind the design is in [decisions.md](decisions.md).

## Getting it running

```bash
docker compose up -d db mailpit          # Postgres :5432, Mailpit :8025 / SMTP :1025
dotnet run --project DashboardBackEnd    # http://localhost:5119
```

`dotnet run` applies pending migrations at startup, so a blank database needs no manual step.
`DashboardBackEnd.http` holds ready-made requests if your editor supports them.

```bash
dotnet test DashboardBackEnd.Tests                                     # needs db + mailpit up
dotnet test DashboardBackEnd.Tests --filter "FullyQualifiedName~AuthTests"
docker compose run --rm tests                                          # same suite in Compose
```

Migrations need the `dotnet-ef` global tool, with `~/.dotnet/tools` on `PATH`:

```bash
dotnet ef migrations add <Name> --project DashboardBackEnd
dotnet ef migrations remove --project DashboardBackEnd
```

Development secrets go in user-secrets, never in a file:

```bash
dotnet user-secrets set "GitHub:ClientSecret" "<value>" --project DashboardBackEnd
```

## Project layout

```
DashboardBackEnd/
  Program.cs                  composition root: options, DI, middleware, Map…Endpoints(), Run
  Auth/                       JWT creation, register/confirm/login/me, role + policy names
  Email/                      IEmailSender and its MailKit/SMTP implementation
  Widgets/                    IService, IWidget, WidgetParam, the registry, catalogue endpoints,
                              config validation, /about.json shapes
  Services/
    ApiClients/               one typed HTTP client per external provider
    <Name>/                   one IService + its IWidget classes
  Subscriptions/              the subscription store, credential encryption, GitHub OAuth
  Instances/                  widget-instance CRUD
  Timer/                      the background refresh loop and its result record
  Admin/                      admin endpoints, the role seeder, AdminOptions
  Models/                     AppUser, ServiceSubscription, WidgetInstance
  Data/                       AppDbContext
  Migrations/                 EF Core migration history
```

Two conventions worth keeping:

- **Endpoints are grouped per feature** in a static class with a `Map…Endpoints()` extension method,
  mapped from `Program.cs`. Handlers are private static methods taking their dependencies as
  parameters — minimal APIs inject them.
- **API clients are separate from service folders.** `Services/ApiClients/GitHubClient.cs` knows how
  to call GitHub; `Services/GitHub/GitHubService.cs` knows what GitHub means to this application. One
  client per provider, reused by every widget of that service.

`Program.cs` ends with `public partial class Program { }` so the test project can name the entry
point. **Do not remove it** — the whole suite stops compiling.

## The widget catalogue

Read [architecture.md](architecture.md#the-two-halves-of-the-domain) for the idea. The mechanics:

`AddWidgetCatalog()` (in `Widgets/WidgetRegistry.cs`) reflects over the assembly, registers every
`IService` and `IWidget` implementation as a singleton, and registers `WidgetRegistry`, which indexes
them by `Key`. The constructor throws on a **duplicate key** or a widget whose **`ServiceKey` matches
no service**, so a mistake fails the boot rather than one request.

Five endpoints project that registry: `/about.json`, `/services`, `/widgets`,
`/widgets/{key}/preview`, `POST /widget-instances`. Adding a class updates all of them.

### `IService`

```csharp
string Key { get; }                 // stored in the database — a constant, never a class name
string Name { get; }
string Description { get; }
AuthKind Auth { get; }              // None | ApiKey | Oauth

CredentialPrompt? CredentialPrompt => null;                       // apiKey services only
Task<bool> ValidateCredentialsAsync(ServiceCredentials, CancellationToken) => true;
```

An `ApiKey` service **should** declare a `CredentialPrompt` (label, help text, a `SuggestedValue` for
providers with a public demo key, a signup URL) and **should** override `ValidateCredentialsAsync`,
so a bad key is rejected before it is stored rather than becoming a permanently failing widget.

### `IWidget`

```csharp
string Key { get; }                 // "<service>.<widget>", stored in the database
string ServiceKey { get; }
string Name { get; }
string Description { get; }
IReadOnlyList<WidgetParam> Parameters { get; }
Task<object> FetchAsync(JsonElement config, ServiceCredentials? credentials, CancellationToken ct);
```

```csharp
record WidgetParam(
    string Name,
    ParamType Type,                 // Integer | String — nothing else exists
    bool Required = true,
    string? Description = null,
    string[]? Options = null,       // a closed list of allowed strings
    int? Min = null, int? Max = null);
```

> **Every widget must declare at least one parameter, and `Type` may only be `Integer` or `String`.**
> Both are delivery requirements, enforced by tests in `AboutTests.cs` that loop over the whole
> catalogue. A widget with no parameter fails the suite.

`WidgetConfigValidator` checks a config against those declarations — required present, types right,
`Min`/`Max` respected, strings within `Options`, and **no unknown key** — and runs both before
storing an instance and before a preview.

### Errors

Raise `WidgetDataException` from `FetchAsync` for failures whose message is safe to show a user
(unknown city, repository not found). It becomes a **422** from `preview` and the `error` field in the
timer's cached result. Any other exception is internal: logged, and replaced by a generic message.

Where several widgets of one service map the same provider failure, do it **once in the client**.
`NasaClient` turns NASA's 429, 403 and 400 into `WidgetDataException`, passing NASA's own `msg`
through, so a key that has used up its quota reads as a rate limit rather than a 500.

Watch for providers that answer a bad request with **200 and an error body** — they cannot be
detected from the status code. CoinGecko returns `{}` for an unknown coin; GitHub returns 200 with an
`error` field when it refuses a code exchange.

## Adding a service or a widget

1. **`Services/ApiClients/<Provider>Client.cs`** — the HTTP client. Use the shared
   `GetFromJsonDocumentAsync` helper. Expose a `const string HttpClientName`. Map provider failures
   to `WidgetDataException` here when several widgets would need the same mapping.
2. **`Services/<Name>/<Name>Service.cs`** — `IService` with a stable `Key` constant and an `AuthKind`.
   For `ApiKey`, add a `CredentialPrompt` and override `ValidateCredentialsAsync`.
3. **`Services/<Name>/<Widget>.cs`** — `IWidget`, declaring `Parameters` and implementing
   `FetchAsync`. At least one parameter.
4. **`Program.cs`** — register the client: `AddSingleton<XClient>()` plus a named `AddHttpClient`
   with a timeout. **The service and widgets need no registration** — the assembly scan finds them.
5. **The test project** — add a `Fake<Provider>Handler` and wire it in `ApiFactory.ConfigureTestServices`,
   so the suite stays offline.

Step 5 is not optional. Without it the suite starts reaching the internet and stops being
deterministic.

### The current catalogue

| Service key | Auth | Provider | Widget keys |
|---|---|---|---|
| `weather` | none | Open-Meteo (+ its geocoding API) | `weather.current`, `weather.forecast` |
| `crypto` | none | CoinGecko | `crypto.price`, `crypto.history` |
| `duolingo` | none | Duolingo's public profile endpoint | `duolingo.profile`, `duolingo.course` |
| `nasa` | apiKey | api.nasa.gov | `nasa.apod`, `nasa.mars` |
| `github` | oauth | GitHub REST API | `github.profile`, `github.repos`, `github.activity` |

## The timer

`Timer/WidgetTimer.cs`, a `BackgroundService`. See
[architecture.md](architecture.md#how-a-widget-gets-its-data) for the shape and
[ADR 009](decisions.md#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache)
for why. What matters when editing it:

- **`GET /widget-instances/data` and `/{id}/data` only read the cache.** No endpoint except `preview`
  may call `FetchAsync`. Do not "fix" a slow dashboard by fetching in the endpoint — that undoes the
  whole design.
- **`POST /{id}/refresh` goes through the timer** (`RefreshNowAsync`): it shares the in-flight guard
  and answers from cache inside `Timer:ManualRefreshCooldownSeconds`, so a user clicking refresh still
  cannot set the provider call rate.
- `Register` marks an instance **due immediately**, which is what makes a newly added or reconfigured
  widget show correct data without waiting out its period. Call it after creating or updating;
  `Unregister` after deleting. `AdminEndpoints.Delete` has to do this too, because the cascade
  removes the rows but the timer keeps its own in-memory schedule.
- Refreshes start **without `await`** behind an in-flight guard; each opens **its own DI scope**
  (the timer is a singleton, `AppDbContext` is scoped); **every failure becomes a stored result**
  that keeps the previous payload. Never let an exception escape the loop.

## Authentication, in code

`AddIdentityCore<AppUser>` + `AddRoles<IdentityRole>()` + the EF Core store + default token
providers. No cookie UI. A hand-written JWT in `Auth/TokenService.cs`: claims `sub`, `name`, `email`
and one `role` per role, HS256, `Jwt:LifetimeMinutes` (60).

Three settings in `Program.cs` are easy to break:

- `MapInboundClaims = false` — without it the short claim names are rewritten to the long
  SOAP-era URIs and nothing finds `sub` any more.
- `NameClaimType = Name`, `RoleClaimType = "role"` — what makes `principal.Identity.Name` and
  `RequireRole` work.
- The `admin` policy is **added**, `DefaultPolicy` is untouched, so every other
  `RequireAuthorization()` behaves as before.

`principal.UserId()` (an extension in `SubscriptionEndpoints`) reads `sub`. Use it for every
ownership filter, and put it **inside the query**:

```csharp
db.WidgetInstances.SingleOrDefaultAsync(w => w.Id == id && w.UserId == principal.UserId(), ct)
```

Another user's row must return **404, not 403**. The admin endpoints are the one documented
exception — see [security.md](security.md#why-admin-answers-403-where-everything-else-answers-404).

## Database and migrations

`AppDbContext : IdentityDbContext<AppUser>`, so Identity's tables and ours share one schema and one
migration history.

| Entity | Notes |
|---|---|
| `AppUser : IdentityUser` | adds `CreatedAt` only |
| `ServiceSubscription` | unique index on *(UserId, ServiceKey)*; cascade delete from the user; `ProtectedCredentials` holds ciphertext |
| `WidgetInstance` | index on `UserId`; cascade delete; `ConfigJson` is text, validated in C#; `RefreshRateSeconds` between 10 and 86 400; `X`/`Y` ≥ 0 and `Width`/`Height` within 1…12, enforced in the endpoint |

Workflow: change the model → `dotnet ef migrations add <Name>` → review the generated `Up`/`Down` →
commit the migration with the model change. `db.Database.Migrate()` at startup applies anything
pending.

> **Do not generate migrations in parallel on the same base.** Two people adding a migration from the
> same snapshot produce a conflicting `AppDbContextModelSnapshot.cs` that is tedious to merge. Agree
> who adds the next one.

The test database is a *separate* database on the same server (`dashboard_test`), created by the
suite's own `Migrate()` call.

## Configuration

Every value comes from `IConfiguration`; nothing is a literal. In Docker these arrive as
`Section__Key` environment variables, which ASP.NET maps to `Section:Key`.

| Key | Used for | Validated on start |
|---|---|---|
| `ConnectionStrings:Default` | Postgres | no (EF fails later) |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Key`, `Jwt:LifetimeMinutes` | signing and validating tokens | **yes** — `Key` ≥ 32 chars |
| `Email:Host`, `Email:Port`, `Email:From` | SMTP | **yes** |
| `Frontend:Url` | confirmation links, OAuth redirects back to the SPA | no |
| `DataProtection:KeysPath` | where the key ring is persisted | no |
| `GitHub:ClientId`, `GitHub:ClientSecret`, `GitHub:CallbackUrl` | the OAuth app | no — **on purpose** |
| `Admin:Email` | who is promoted to administrator | no — **on purpose** |
| `Oidc:Google:ClientId`, `…:ClientSecret`, `…:CallbackUrl` | sign in with Google | no — **on purpose** |
| `Oidc:Microsoft:ClientId`, `…:ClientSecret`, `…:CallbackUrl`, `…:Tenant` | sign in with Microsoft (`Tenant` is Epitech's tenant id in our configuration, `common` if unset) | no — **on purpose** |
| `Timer:ManualRefreshCooldownSeconds` | how recent a fetch makes `POST /widget-instances/{id}/refresh` answer from cache (default 10) | **yes** — 0…3600 |

`JwtOptions` and `EmailOptions` use `ValidateDataAnnotations().ValidateOnStart()`: a missing or
too-short key fails the boot with a clear message. `GitHubOptions` and `AdminOptions` are bound
**without** it, because a fresh clone has no OAuth app and no administrator and must still run — the
OAuth endpoints answer **503** while unset, and `/admin` is simply unreachable.

Where each secret belongs is in [security.md](security.md#where-each-secret-lives).

## Tests

`DashboardBackEnd.Tests` is an integration suite: **193 tests across 13 classes**. `ApiFactory` boots
the real API in-process with `WebApplicationFactory<Program>` against the real Postgres and Mailpit,
swapping only the database name to `dashboard_test`. External providers are stubbed with fake
`HttpMessageHandler`s, so **the suite runs offline and must stay that way.**

| Piece | Role |
|---|---|
| `ApiFactory` | the real app, test database, fake provider handlers. It caps the shared connection pool at 40, under Postgres's 100, and once per run deletes the widget instances left by earlier runs. Every host's timer refreshes all instances it finds at boot, and the hundreds that piled up used to exhaust the connections (`53300: too many clients`) |
| `ApiFactoryWithStubs` | additionally registers stub `apiKey`/`oauth` services, which join the registry for those tests only — how the subscription layer is covered without a real credentialed service |
| `ApiFactoryWithAdmin` | configures `Admin:Email` so the seeding paths can be exercised |
| `TestUser.CreateAndLoginAsync` | creates a user with a **unique email** and returns an authenticated client. Every test does this, because the test database persists between runs (only widget instances are cleared). A failed register, confirm or login throws with the step's name and response |
| `MailpitClient` | reads confirmation links back out of Mailpit by recipient — the suite plays the part of the user opening their inbox |
| `Fake*Handler` | one per provider, returning canned payloads and the error shapes we care about |

Timer tests poll `/data` until a status appears, exercising the real background service rather than a
stub of it. They are the slowest tests in the suite; that is inherent.

Writing a new test: create a user, act through `HttpClient`, assert on status codes and bodies. Do
not reach into `AppDbContext` to set up state unless the test is specifically about something the API
cannot express — the suite's value is that it tests observable behaviour.
