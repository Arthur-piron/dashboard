# Architecture

How the system is put together, and why it is split this way. For the reasoning behind each
individual choice, see [decisions.md](decisions.md); for the stack itself, [technology.md](technology.md).

## The whole stack

Four containers, described in one `docker-compose.yml`.

```
                    ┌───────────────────────────────────────────┐
   browser  ───────▶│  client  ·  nginx :80   (published :8081) │
                    │                                           │
                    │   /          → the built React app        │
                    │   /api/...   → http://server:8080/...     │
                    └───────────────────┬───────────────────────┘
                                        │  (inside the Compose network)
                    ┌───────────────────▼───────────────────────┐
                    │  server  ·  ASP.NET Core :8080  (:8080)   │
                    │                                           │
                    │   endpoints  ·  widget catalogue          │
                    │   WidgetTimer (background service)        │
                    └───┬────────────────┬──────────────┬───────┘
                        │                │              │
             ┌──────────▼─────┐  ┌───────▼──────┐  ┌────▼─────────────────┐
             │ db · Postgres  │  │ mailpit      │  │ the internet         │
             │ users, subs,   │  │ SMTP + web   │  │ Open-Meteo, CoinGecko│
             │ widget rows    │  │ inbox :8025  │  │ NASA, Duolingo,GitHub│
             └────────────────┘  └──────────────┘  └──────────────────────┘
```

The browser only ever talks to **one** origin, `:8081`. The API is published on `:8080` as well,
because the delivery requires `/about.json` to be reachable there, but the front end never uses
it: all its calls are relative `/api/...` paths that nginx rewrites. That is what lets the same
built image run on a laptop, in CI, or on a server, with no rebuild and no CORS configuration
([ADR 012](decisions.md#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url)).

## The two halves of the domain

This is the single most important idea in the codebase.

> **What the platform offers lives in code. What a user chose lives in the database.**

A *service* exists because a C# class implements `IService`. A *widget* exists because a class
implements `IWidget`. There is no services table, no widgets table, and no seed data for them.
At startup, `AddWidgetCatalog()` reflects over the assembly, registers every implementation as a
singleton, and `WidgetRegistry` indexes them by key.

The database holds only per-user state:

| Table | Holds |
|---|---|
| `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserLogins`, … | accounts, roles, linked external identities (ASP.NET Identity) |
| `ServiceSubscriptions` | which user may use which service, with the credentials **encrypted** |
| `WidgetInstances` | a widget key, the user's configuration as JSON, a refresh rate, a grid position |

The bridge between the two halves is a **string key**: `"weather"`, `"weather.current"`. Those
strings are stored in the database, so they are declared as constants and never derived from a
class name — renaming a C# class must not orphan stored rows
([ADR 005](decisions.md#adr-005--string-keys-as-the-contract-between-code-and-database)).

The payoff: five endpoints read the same registry, so adding one class makes a new service or
widget appear in all of them at once — `/about.json`, `/services`, `/widgets`,
`/widgets/{key}/preview`, and `POST /widget-instances`. Adding a widget means adding a file,
not editing six.

Two consistency checks run in the `WidgetRegistry` constructor, i.e. at boot: a duplicate key,
or a widget whose `ServiceKey` matches no service, **fails the startup** rather than surfacing as
a confusing 500 on some request later.

## How a widget gets its data

The chain is deliberately unusual, and it is the part most likely to be "fixed" by mistake.

```
  the browser                  the server                     a provider
  ───────────                  ──────────                     ──────────

  GET /api/widget-instances/7/data
         │
         ▼
   reads the cache ◀─────── WidgetTimer ──── every second: who is due? ───▶ FetchAsync
   (never fetches)            ▲    │                                           │
                              │    └── stores the result ◀─────────────────────┘
                              │
                     schedule: instance id → next due
```

`Timer/WidgetTimer.cs` is a `BackgroundService` with a one-second `PeriodicTimer`, a schedule
(`instance id → next due at`) and a result cache. Instances enter the schedule at startup, loaded
from the database, and afterwards through `Register`/`Unregister`, which the instance endpoints
call when a widget is created, reconfigured or deleted. `Register` marks an instance due
**immediately**, which is why a widget you have just added shows real data at once instead of
waiting out its period.

`GET /widget-instances/data` and `/widget-instances/{id}/data` **only read that cache**. No
endpoint except `preview` ever calls `FetchAsync`. The consequence is the point of the design:
how often the browser polls and how often a provider is called are completely independent. Ten
open tabs on a 10-second widget still produce one provider call per 10 seconds.

Three details inside the loop are load-bearing:

- A refresh is started **without `await`**, behind an in-flight guard, so one slow provider
  cannot stall the tick for every other widget.
- Each refresh opens **its own DI scope**, because the timer is a singleton while `AppDbContext`
  is scoped.
- **Every failure becomes a stored result** that keeps the previous payload and adds an `error`
  message. An exception never escapes the loop, so the background service cannot die and leave
  the dashboard frozen.

A widget result is therefore always one of three states — `pending`, `ok`, `error` — and an
`error` still carries the last good data, so a provider hiccup dims a widget instead of blanking it.

## Errors that are safe to show

`FetchAsync` raises `WidgetDataException` for failures whose message a user should see: unknown
city, repository not found, rate limit exhausted. It becomes a `422` from `preview` and the
`error` field in the timer's cached result. Any other exception is treated as internal: it is
logged, and the user sees a generic "could not be refreshed" message.

Where several widgets of one service would map the same provider failure, the mapping is done
once in the API client instead. `NasaClient` turns NASA's 429, 403 and 400 into
`WidgetDataException`, passing NASA's own message through, so a key that has used up its quota reads
as a rate limit rather than a 500. Providers that answer a bad request with **200 and an error body**
need the same care and cannot be handled by status code alone: CoinGecko returns `{}` for an
unknown coin, and GitHub returns 200 with an `error` field when it refuses a code exchange.

## Two kinds of authorisation, never mixed

| | Authenticating *our* user | Authorising *us* to a third party |
|---|---|---|
| Mechanism | ASP.NET Identity + a JWT we sign | an API key or an OAuth access token |
| Stored in | `AspNetUsers` (password hash) | `ServiceSubscriptions.ProtectedCredentials`, encrypted |
| Carried as | `Authorization: Bearer <jwt>` on every call | never leaves the server |
| Lives for | 60 minutes | until the user unsubscribes |

Keeping these separate is why linking a GitHub account and logging in are different features:
one stores a token for calling GitHub on your behalf, the other would let GitHub vouch for who
you are. The first exists; the second is future work.

Ownership is enforced **in the query**, not after it: every instance and subscription lookup
filters by the JWT's user id in the same `WHERE`. Someone else's row returns **404, not 403**, so
the existence of an id is never revealed. The admin endpoints deliberately break that pattern and
return **403** to a logged-in non-admin, because a role gate is not an ownership check —
see [security.md](security.md#why-admin-answers-403-where-everything-else-answers-404).

## Linking a GitHub account

The authorisation code flow, written by hand
([ADR 011](decisions.md#adr-011--the-oauth-code-flow-by-hand-with-a-signed-time-limited-state)):

```
 1. SPA  ──▶ GET /services/github/oauth/authorize    (with the JWT)   → { url }
 2. SPA  ──▶ navigates the browser to that GitHub URL
 3. user ──▶ approves on github.com
 4. GitHub ─▶ GET /services/github/oauth/callback?code=…&state=…      (no JWT!)
 5. server ─▶ exchanges the code for a token, stores it encrypted
 6. server ─▶ 302 to {Frontend:Url}/services?github=linked&account=…
```

Step 4 is **anonymous on purpose**: GitHub redirects a browser, and a browser navigation carries
no `Authorization` header. What ties the callback back to a user is the `state` — the user id
sealed with a *time-limited* Data Protection protector (10 minutes). It is unforgeable without the
server's keys and expires on its own, so the flow needs no table and no migration. A plain user id
in `state` would let anyone attach their GitHub account to someone else's Dashboard user.

Two consequences worth preserving:

- Every failure after the configuration check is a **redirect**, not a problem document. A JSON
  error would land in the address bar where the user can do nothing with it.
- The route is `/services/github/oauth/…` rather than a tidier `/oauth/github/…` because it has
  to match the callback URL registered on the GitHub OAuth app. Changing the route means editing
  the app's settings.

The token is stored as an ordinary subscription to the `github` service, so
`DELETE /subscriptions/github` already unlinks the account.

## Starting up

`Program.cs`, in order:

1. Bind and validate configuration. `JwtOptions` and `EmailOptions` use
   `ValidateDataAnnotations().ValidateOnStart()` — a missing or too-short signing key fails the
   boot with a clear message instead of producing unverifiable tokens. `GitHubOptions` and
   `AdminOptions` are bound **without** `ValidateOnStart`, because a fresh clone has no OAuth app
   and no configured administrator and must still run.
2. Register Identity, JWT bearer authentication, the `admin` policy, Data Protection, the widget
   catalogue, the timer, and one typed client per provider.
3. In a startup scope: `db.Database.Migrate()` applies pending migrations, then `AdminSeeder`
   creates the `Admin` role and promotes the configured administrator if that account exists.
4. Map the endpoint groups and run.

A blank database therefore needs no manual step: start the server and it migrates itself.

## Where to look in the code

| Folder | Holds |
|---|---|
| `Auth/` | JWT creation, the endpoints for register/confirm/login/me, role and policy names |
| `Email/` | the `IEmailSender` abstraction and its SMTP implementation |
| `Widgets/` | `IService`, `IWidget`, `WidgetParam`, the registry, the catalogue endpoints, config validation |
| `Services/ApiClients/` | one typed HTTP client per external provider |
| `Services/<Name>/` | one service class and its widget classes |
| `Subscriptions/` | the subscription store, credential encryption, the GitHub OAuth endpoints |
| `Instances/` | the widget-instance CRUD endpoints |
| `Timer/` | the background refresh loop and its result record |
| `Admin/` | the admin endpoints, the role seeder |
| `Data/`, `Migrations/` | the EF Core context and the schema history |
