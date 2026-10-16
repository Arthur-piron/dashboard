# Design decisions

Every entry below records one decision: the problem it answers, the options we actually weighed,
what we chose, and what the choice costs us. They are append-only — a decision that is later
reversed gets a new entry rather than an edit, so the reasoning stays readable in order.

Every entry has the same parts, so each question has a fixed place to look:

| Question | Part of each entry |
|---|---|
| Why this choice? | **Context**, then **Decision** |
| What else did we consider, and why not? | **Options**, a table with the reason each was rejected |
| What does it change, for better and for worse? | **What it buys** and **What it costs** |
| What would we do differently next time? | **Alternative we would propose for a second iteration** |
| When should it be reopened? | **Revisit if**, where a clear trigger exists |

The comparisons between technologies, layer by layer, are in [technology.md](technology.md#comparisons-layer-by-layer).

A decision is in here if changing it would mean changing several files. Local choices (a variable
name, a CSS value) are not decisions.

| # | Decision | Status |
|---|---|---|
| [001](#adr-001--net-10-minimal-api-for-the-back-end) | .NET 10 minimal API for the back end | accepted |
| [002](#adr-002--react--vite-in-plain-javascript-no-typescript) | React + Vite in plain JavaScript, no TypeScript | accepted |
| [003](#adr-003--postgresql-with-ef-core-migrations) | PostgreSQL with EF Core migrations | accepted |
| [004](#adr-004--the-catalogue-lives-in-code-user-choices-live-in-the-database) | The catalogue lives in code, user choices live in the database | accepted |
| [005](#adr-005--string-keys-as-the-contract-between-code-and-database) | String keys as the contract between code and database | accepted |
| [006](#adr-006--aspnet-identity-core-without-its-ui) | ASP.NET Identity Core, without its UI | accepted |
| [007](#adr-007--a-signed-jwt-in-localstorage-rather-than-a-session-cookie) | A signed JWT in `localStorage` rather than a session cookie | accepted, with a known cost |
| [008](#adr-008--email-confirmation-required-before-the-first-login) | Email confirmation required before the first login | accepted |
| [009](#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache) | One background timer owns every fetch; endpoints only read a cache | accepted |
| [010](#adr-010--third-party-credentials-encrypted-with-aspnet-data-protection) | Third-party credentials encrypted with ASP.NET Data Protection | accepted |
| [011](#adr-011--the-oauth-code-flow-by-hand-with-a-signed-time-limited-state) | The OAuth code flow by hand, with a signed, time-limited state | accepted |
| [012](#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url) | Relative API paths behind nginx, rather than a configured API URL | accepted |
| [013](#adr-013--roles-in-the-jwt-and-an-administrator-bootstrapped-from-configuration) | Roles in the JWT, and an administrator bootstrapped from configuration | accepted, with a known cost |
| [014](#adr-014--integration-tests-against-a-real-database-with-stubbed-providers) | Integration tests against a real database, with stubbed providers | accepted |
| [015](#adr-015--every-value-from-iconfiguration-never-a-literal) | Every value from `IConfiguration`, never a literal | accepted |
| [016](#adr-016--sign-in-with-google-and-microsoft-oidc-by-hand) | Sign in with Google and Microsoft, OIDC by hand | accepted, amends 013 |

---

## ADR 001 — .NET 10 minimal API for the back end

**Context.** The subject leaves the stack free. We needed an HTTP API with real authentication,
background work, a relational database and tests, built by two students in a few weeks.

**Options.**

| Option | Why not |
|---|---|
| **Node.js + Express** | Fastest to start, and the one we knew best. But authentication, password hashing, email confirmation tokens and roles are all assembled from third-party packages chosen and wired by hand; background scheduling is another. We would have spent the project's budget on plumbing that the alternative ships. |
| **Python + FastAPI** | Excellent ergonomics, but the same assembly problem for identity, and neither of us had used it on a project of this size. |
| **ASP.NET Core with MVC controllers** | Same platform, more ceremony: a controller class, attributes and a base type per group of routes. The minimal API expresses the same routes as a few lines in an extension method. |
| **.NET 10 minimal API** | **Chosen.** Identity, EF Core, `BackgroundService`, Data Protection, options validation and `WebApplicationFactory` tests are all in the framework, documented together and versioned together. |

**Decision.** .NET 10 with the minimal API model, routes grouped by feature in
`Map…Endpoints()` extension methods.

**What it buys.** The features that would have been the riskiest to hand-roll — password hashing,
email confirmation tokens, role management, credential encryption, a scheduler, in-process
integration tests — are first-party and were not written by us. Startup validation of
configuration comes free, so a missing signing key is a boot failure with a message rather than a
subtle runtime bug.

**What it costs.** It is the least common choice in the promotion, so there is less help available
from classmates. The SDK image is large, which makes the first Docker build slow. Minimal APIs
trade the discoverability of a controller class for brevity: the only way to see every route is to
read `Program.cs` and follow the `Map…` calls.

**Alternative we would propose for a second iteration.** Keep .NET, and remove its two costs. Publish an OpenAPI document with the built-in `Microsoft.AspNetCore.OpenApi` (`AddOpenApi` + `MapOpenApi`), so every route is discoverable without reading `Program.cs`. Build the runtime image on a smaller base (the `-alpine` or chiseled `aspnet` images), since ours is 363 MB today.

**Revisit if** the project needed real-time server push everywhere, or a team unfamiliar with .NET
had to maintain it.

---

## ADR 002 — React + Vite in plain JavaScript, no TypeScript

**Context.** The interface is a grid of configurable cards with a light/dark theme, built from a
Figma mockup. The repository was scaffolded from Vite's React + TypeScript template.

**Options.**

| Option | Why not |
|---|---|
| **Next.js** | Its server rendering and routing solve problems we do not have: the app is behind a login, needs no SEO, and ships as static files behind nginx. It would add a Node process to the deployment. |
| **Angular** | A strong fit for large structured apps, but the amount of framework to learn is out of proportion for two students and a dozen components. |
| **Vue** | Would have worked. React won on familiarity within the group, nothing more — this is a preference, and we record it as one. |
| **React + TypeScript** | What the template produced. We removed it deliberately, see below. |
| **React 19 + Vite, plain `.jsx`** | **Chosen.** |

**Decision.** React 19 with Vite, in plain JavaScript. `tsconfig*.json`, `typescript` and the
`@types/*` packages were removed and ESLint reconfigured for `.js`/`.jsx`. Styling is plain CSS,
one stylesheet per component — no Tailwind, no CSS-in-JS, no component library. State is `useState`
and `useEffect`; the only shared state is the theme, which is not enough to justify Redux, Zustand
or even a Context.

**What it buys.** One language to think about on the front end, no build-time type errors to fight
while the API's shapes are still moving, and a dependency list short enough to audit at a glance:
`react`, `react-dom`, `react-router-dom`. Vite's dev server starts instantly and its production
build is a folder of static files, which is exactly what nginx wants.

**What it costs.** This is the decision we are least comfortable with. Without TypeScript, nothing
checks that the front end reads the shape the API actually returns — a renamed field surfaces as
`undefined` in the browser rather than as a build error, and the only safety net is testing by
hand. Plain CSS per component means the design tokens in `index.css` are a convention, not an
enforced contract.

**Alternative we would propose for a second iteration.** Reintroduce TypeScript for the API
boundary alone: typed response interfaces plus a single typed `fetch` wrapper, leaving component
internals in JavaScript. That recovers most of the safety for a fraction of the migration.

**Revisit if** the front end grows past roughly twenty components, or a second person starts
working in it simultaneously.

---

## ADR 003 — PostgreSQL with EF Core migrations

**Context.** The data is small, highly relational and always queried per user: accounts, their
subscriptions, their widget instances. The schema was expected to change several times.

**Options.**

| Option | Why not |
|---|---|
| **SQLite** | Zero setup, but a single file is awkward to share between containers and is not what the application would run on in production. Testing against a different engine than we ship hides engine-specific problems. |
| **MongoDB** | The access pattern is relational — users own subscriptions own instances — and we want the database to enforce the uniqueness of *(user, service)* and to cascade deletes. Modelling that in a document store means enforcing it in application code instead. |
| **Raw SQL or Dapper** | More control, full ownership of the schema, and no magic. But ASP.NET Identity ships an EF Core store; using Dapper would have meant writing the identity schema and its queries ourselves, which is work with no payoff here. |
| **PostgreSQL + EF Core** | **Chosen.** |

**Decision.** PostgreSQL 17 in a container, EF Core with code-first migrations. The context derives
from `IdentityDbContext<AppUser>`, so the identity tables and ours live in one schema and one
migration history. `db.Database.Migrate()` runs at startup.

**What it buys.** The constraints live in the database: a unique index on *(UserId, ServiceKey)*
makes a double subscription impossible, and `OnDelete(Cascade)` means deleting an account cannot
leave orphaned widget rows behind. Migrating at startup means a fresh clone needs no manual step,
which matters when a grader runs `docker compose up` once.

**What it costs.** Migrating at startup is wrong for a multi-instance deployment — two servers
booting together would race on the same schema. Two people generating migrations from the same
base produce a conflict that is tedious to untangle, so migrations have to be coordinated.
The configuration JSON stored in `WidgetInstances.ConfigJson` is a `text` column we validate in C#,
not a `jsonb` column the database could check, which is a deliberate simplification.

**Alternative we would propose for a second iteration.** Move migrations out of startup into a one-shot `migrate` service in Compose, running an EF Core migration bundle (`dotnet ef migrations bundle`) before `server` starts. Two replicas can then boot safely. Store `ConfigJson` as `jsonb`, so the database can index and check what today only C# validates.

**Revisit if** the API is ever scaled to more than one replica — then the migration must move to an
explicit step in the deployment.

---

## ADR 004 — The catalogue lives in code, user choices live in the database

**Context.** The application has two kinds of information that are easy to confuse: *what the
platform offers* (the services and widgets, their parameters, how to call a provider) and *what a
user chose* (which services they subscribed to, which widgets they placed, how they configured
them).

**Options.**

| Option | Why not |
|---|---|
| **Both in the database.** A `services` table and a `widgets` table, seeded by a migration, each row describing a widget and its parameters. | Every new widget then needs two things that must agree: a row and the code that fetches its data. They drift. Adding a widget becomes a migration, which is heavy, and the parameter list has to be modelled generically as rows, which loses all type information. |
| **Both in code.** Store subscriptions and instances in memory or in a JSON file next to the server. | User state must survive a restart and be queryable per user. This is what a database is for. |
| **The catalogue in code, user state in the database.** | **Chosen.** |

**Decision.** A service exists if and only if a class implements `IService`; a widget, if a class
implements `IWidget`. `AddWidgetCatalog()` reflects over the assembly at startup and
`WidgetRegistry` indexes them. The database stores only `ServiceSubscription` and `WidgetInstance`.

**What it buys.** Adding a widget is adding one file — no migration, no registration, no seed. Its
parameters are declared as a C# list, so they are typed and cannot disagree with the code that
reads them. Five endpoints project the same registry, so they can never fall out of sync with each
other. Two consistency checks run in the registry's constructor, which means a duplicate key or a
widget pointing at a service that does not exist **fails the boot** instead of failing one request
in production.

**What it costs.** The catalogue cannot change without a deployment — no administrator can add a
service from a UI, and there is no per-service feature flag. Reflection over the assembly is
implicit: a class that implements `IWidget` is live the moment it compiles, which is convenient
but means an unfinished widget left in the repository appears in the catalogue. That caught us
once, and it is the reason a half-written widget should sit on a branch rather than in `main`.

**Alternative we would propose for a second iteration.** Keep the catalogue in code, and add a per-deployment switch in configuration (for example `Catalog:Disabled`) that the registry reads at startup. A deployment can then hide a service without a rebuild, and without moving the catalogue into the database and losing the compile-time contract.

**Revisit if** services ever need to be enabled per deployment or managed at runtime.

---

## ADR 005 — String keys as the contract between code and database

**Context.** Stored rows have to point at a service and a widget. Something has to be the
identifier, and it ends up written into the database in every subscription and every widget
instance.

**Options.**

| Option | Why not |
|---|---|
| **The class name**, read by reflection. | Free to write and always unique. But it makes every C# rename a silent data migration: rename `WeatherService` and every stored row points at a service that no longer exists. A refactoring tool would corrupt data. |
| **A numeric id** in an enum or a lookup table. | Compact, but meaningless in the database — debugging a row means consulting a mapping — and reordering an enum rewrites the meaning of existing rows. |
| **An explicit string constant.** | **Chosen.** |

**Decision.** Keys are `const string` declarations — `"weather"`, `"weather.current"`, and
likewise `AppRoles.Admin = "Admin"` for the role name and policy. Nothing is derived from a type
name. The widget key is namespaced by its service, which makes a row self-explanatory.

**What it buys.** C# can be refactored freely; only an intentional edit to a constant touches
stored data. A database row is readable without a decoder. The keys are also the public API: they
appear in `/about.json` and in the front end's widget-to-component map, so one vocabulary spans
the whole system.

**What it costs.** The compiler cannot catch a typo in a key, and nothing prevents two classes
declaring the same one — which is exactly why the registry checks for duplicates at startup. The
convention that a widget key is prefixed by its service key is enforced by review, not by code.

**Alternative we would propose for a second iteration.** Keep string keys, but enforce the conventions in a test rather than in review: a catalogue test asserting that every widget key starts with its service key followed by a dot. Declare each service's keys as constants in one static class, so a typo is a compile error.

---

## ADR 006 — ASP.NET Identity Core, without its UI

**Context.** We need accounts: unique emails, hashed passwords, confirmation tokens, roles, and
later a record of external logins. Getting any of this subtly wrong is a security bug.

**Options.**

| Option | Why not |
|---|---|
| **Our own users table.** A password column, a hash function, our own tokens. | The failure modes are severe and silent: a weak hash, a token that is guessable or never expires, a timing-attack-prone comparison. There is no reason to accept that risk for a feature every framework ships. |
| **Full ASP.NET Identity** (`AddIdentity` + the Razor UI). | Brings cookie authentication, scaffolded Razor pages and a `/Identity/Account/...` area. We serve a React SPA and authenticate with a bearer token, so all of it would be dead weight — and the cookie scheme it installs would compete with ours. |
| **Identity Core** (`AddIdentityCore`) + our own endpoints. | **Chosen.** |

**Decision.** `AddIdentityCore<AppUser>` with `AddRoles`, the EF Core store and the default token
providers. Our own minimal-API endpoints for register, confirm, login and me. `AppUser` extends
`IdentityUser` with one field, `CreatedAt`. Password policy: at least 8 characters; non-alphanumerics
not required, but Identity's defaults still demand an upper case letter, a lower case letter and a
digit.

**What it buys.** `UserManager` handles hashing, uniqueness, confirmation tokens, lockout and role
membership. We own the HTTP surface — JSON in, JSON out, our own status codes and error shapes —
without owning the cryptography.

**What it costs.** `AddIdentityCore` installs no authentication scheme, so the JWT handler and
everything about *reading* an identity from a request is ours to configure correctly — and
`MapInboundClaims = false` plus explicit `NameClaimType`/`RoleClaimType` is the kind of detail that
is easy to get wrong and hard to notice. Identity's error codes (`PasswordTooShort`,
`DuplicateEmail`) leak into our 400 responses, which couples our API's error bodies to a framework
vocabulary.

**Alternative we would propose for a second iteration.** Keep Identity Core, and translate its error codes into our own vocabulary in one place (`PasswordTooShort` → `password_too_short`). The API's error bodies then stop depending on the framework's names. Add an integration test that reads the claims back through `/auth/me`, so a wrong `MapInboundClaims` setting fails a test rather than a user.

---

## ADR 007 — A signed JWT in `localStorage` rather than a session cookie

**Context.** A React app on one origin talks to an API behind the same origin. Every request has
to carry proof of who the user is.

**Options.**

| Option | Why not |
|---|---|
| **An `httpOnly` session cookie.** | The safest default: JavaScript cannot read the token, so an XSS flaw cannot steal it. But it requires server-side session state or a cookie-auth scheme, needs CSRF protection on every mutating request, and cookie scope/`SameSite` rules get awkward when the API is also published on a second port for the delivery check. |
| **An `httpOnly` cookie holding a short JWT plus a refresh token.** | The right answer for production, and strictly better than what we chose. It is also two more endpoints, a rotation and revocation story, and a per-request CSRF token — a feature of its own, which we could not fund alongside the required scope. |
| **A JWT in `localStorage`, sent as a bearer header.** | **Chosen.** |

**Decision.** `Auth/TokenService.cs` signs an HS256 token with claims `sub`, `name`, `email` and
one `role` claim per role, valid for `Jwt:LifetimeMinutes` (60 by default). The front end stores it
under `auth-token` and sends `Authorization: Bearer <token>`.

**What it buys.** The API is stateless: no session store, no sticky sessions, no CSRF surface,
because an attacker's page cannot make the browser attach a header the way it attaches a cookie.
Authorisation is a claim check with no database round trip — `/auth/me` reads the token only.
It is also the most teachable version of the mechanism, which matters for a school project we have
to defend.

**What it costs.** We accept a real, named risk: **any script running on our origin can read the
token.** We have no XSS today, but the mitigation that `httpOnly` would give us for free is simply
absent. There is also no logout that invalidates a token server-side — "logging out" deletes the
local copy, and a token already copied elsewhere stays valid until it expires.

**Alternative we would propose for a second iteration.** Move the token into an `httpOnly`, `SameSite=Strict` cookie holding a short-lived JWT (about 10 minutes), with a rotating refresh token stored server-side. An XSS can no longer read the token, and logout becomes real revocation. The claims and the authorisation code stay as they are; only where the token travels changes.

**Revisit** before anything resembling production use. The migration path is the second option
above, and it does not change the claims or the authorisation code — only where the token is kept.

---

## ADR 008 — Email confirmation required before the first login

**Context.** Accounts are created by anyone who can reach the form. The administrator is identified
by an email address (see [ADR 013](#adr-013--roles-in-the-jwt-and-an-administrator-bootstrapped-from-configuration)),
so an unverified address is not only a spam problem but a privilege-escalation problem.

**Options.**

| Option | Why not |
|---|---|
| **No confirmation.** Register, log in immediately. | Simplest, and tempting for a graded project where every extra step is a step a grader has to perform. But then anyone may register as the configured administrator's address and be promoted on the spot. |
| **Confirmation, with a real SMTP provider in development.** | Needs credentials in the repository or in every developer's environment, sends real mail during tests, and breaks offline. |
| **Confirmation, delivered to a local mail catcher.** | **Chosen.** |

**Decision.** `SignIn.RequireConfirmedEmail = true`. Registration returns `202 Accepted` and sends
a link to `{Frontend:Url}/confirm?userId=…&token=…`; the token is URL-safe base64 of Identity's own
confirmation token. Logging in before confirming returns **403** with `type: "email_not_confirmed"`,
which is a deliberate, documented behaviour and not a bug. Mail goes through an `IEmailSender`
abstraction to **Mailpit**, a container with an SMTP server and a web inbox on `:8025`, in both
development and Docker.

**What it buys.** Promotion to administrator is gated by proving ownership of the address. The flow
is identical in development, in Docker and in the tests, because only `Email:Host` changes — and the
test suite reads confirmation links back out of Mailpit, so it exercises the real email path rather
than a stub of it.

**What it costs.** Three steps to a usable account instead of one, which has to be explained to
anyone running the project — hence the mailbox link in the README. Registration answers `202` even
when sending the mail failed: the exception is logged and swallowed, because failing the
registration would be worse. The practical effect is that a user whose mail never arrives is stuck
with no way to ask for another one; **resending a confirmation is a missing feature.**

**Alternative we would propose for a second iteration.** Add `POST /auth/resend-confirmation`, which always answers `202` whether or not the address exists, so it reveals nothing about accounts, and is rate-limited per address. That turns the stuck user into one click instead of a call for help.

---

## ADR 009 — One background timer owns every fetch; endpoints only read a cache

**Context.** Each widget instance has its own refresh rate, from 10 seconds to a day. Several
users, several instances, and providers that are slow, rate-limited, or occasionally down.

**Options.**

| Option | Why not |
|---|---|
| **Fetch when the browser asks.** `GET /…/data` calls the provider. | Simplest, and wrong. The provider call rate becomes the browser polling rate times the number of open tabs; the user's refresh-rate setting means nothing; a slow provider becomes a slow endpoint; and a rate-limited key is exhausted by a page refresh. |
| **A timer per instance** (`System.Timers.Timer` each). | Thread and memory cost grows with the number of widgets, and nothing coordinates them — there is no single place to inspect or bound the work. |
| **A scheduler library** (Hangfire, Quartz). | Built for durable, distributed jobs with persistence and retries. Here the unit of work is "call one API and keep the answer in memory"; the library's storage and dashboard would be more moving parts than the problem has. |
| **One `BackgroundService` with a schedule and a cache.** | **Chosen.** |

**Decision.** `WidgetTimer`, a hosted service with a one-second `PeriodicTimer`, a
`ConcurrentDictionary` schedule (`instance id → next due`), a result cache and an in-flight guard.
Instances are loaded at startup and registered or unregistered by the instance endpoints;
`Register` marks an instance due immediately. The data endpoints only read the cache — **no endpoint
except `preview` ever calls `FetchAsync`.**

**What it buys.** Polling rate and provider call rate are independent, so the dashboard can poll
freely while a widget on a one-hour rate calls its provider once an hour. Data endpoints are fast
and cannot fail because a provider failed. Refreshes run without `await` behind the in-flight
guard, so one slow provider does not stall the loop; each refresh opens its own DI scope, because
the timer is a singleton and the database context is scoped; and every failure becomes a stored
result that keeps the previous payload, so the loop cannot die and a hiccup dims a widget instead
of blanking it.

**What it costs.** The cache is in memory, so a restart means every widget is `pending` again until
its first refresh. The schedule is in memory too, which makes the timer **inherently
single-instance**: two API replicas would each refresh every widget. Work is also not bounded —
nothing caps how many refreshes may run at once, which is fine at this scale and would not be with
thousands of instances. And the design is easy to "fix" wrongly: making a slow dashboard faster by
fetching in the endpoint would undo all of the above, which is why it is stated so bluntly here and
in the code.

**Alternative we would propose for a second iteration.** Keep one timer, and persist each instance's last result in a small table, so a restart shows the previous data instead of `pending`. Cap concurrent refreshes with a `SemaphoreSlim`. For more than one replica, move the schedule to the database with a row lock per refresh (or a single elected worker), so each widget is still refreshed once.

**Revisit if** the API needs more than one replica, or if results must survive a restart — then the
cache becomes Redis and the schedule becomes a table or a distributed lock.

---

## ADR 010 — Third-party credentials encrypted with ASP.NET Data Protection

**Context.** Subscribing to NASA means giving us an API key; linking GitHub means we hold an access
token. These are the user's credentials at someone else's service. Unlike a password, we must be
able to *read* them back to make calls, so hashing is not an option.

**Options.**

| Option | Why not |
|---|---|
| **Store them as they are.** | A read of one table — a backup, a log of a query, a leaked dump — hands out every user's keys and tokens. Not acceptable for data that is not ours. |
| **Encrypt in the database** (`pgcrypto`, or Transparent Data Encryption). | Protects the files at rest, but the application sends and receives plaintext and the key lives in the database or its configuration. It defends against a stolen disk, not against a stolen dump. |
| **A secret manager** (Vault, AWS KMS, Azure Key Vault). | The correct production answer: managed keys, rotation, audit. It is also an external dependency with credentials of its own, which a project that must run from `docker compose up` on a fresh clone cannot require. |
| **ASP.NET Data Protection.** | **Chosen.** |

**Decision.** `Subscriptions/CredentialProtector.cs` serialises a `ServiceCredentials` record to
JSON and protects it with a purpose-scoped protector; the ciphertext goes in
`ServiceSubscriptions.ProtectedCredentials`. Keys are persisted to `DataProtection:KeysPath`, the
`/keys` volume in Compose. The same mechanism, in its *time-limited* form, seals the OAuth `state`
([ADR 011](#adr-011--the-oauth-code-flow-by-hand-with-a-signed-time-limited-state)).

**What it buys.** A database dump is useless on its own — the ciphertext cannot be read without the
key ring. Key generation, rotation and expiry are handled by the framework, and the purpose string
means a credential blob cannot be replayed as an OAuth state or vice versa. **No endpoint ever
returns a credential**, and the test suite asserts both halves of that: nothing leaks through the
API, and the stored column really does hold ciphertext.

**What it costs.** The key ring becomes critical state. **Losing `/keys` makes every stored
credential permanently unreadable, even with a perfectly intact database** — users would have to
re-enter their keys and re-link GitHub. The volume must be backed up with the database and, in a
multi-server deployment, shared between servers. The keys are also only as protected as the
filesystem holding them: unlike a secret manager, there is no envelope encryption and no audit
trail of which process read a key.

**Alternative we would propose for a second iteration.** Protect the key ring itself: `ProtectKeysWithCertificate`, or a cloud KMS (Azure Key Vault, AWS KMS) through Data Protection's built-in extensions. Back up `/keys` together with the database in the same job. With several servers, store the key ring where all of them read it (a shared volume or the database).

**Revisit** before any real deployment: Data Protection supports protecting the key ring itself
with a certificate or a cloud KMS, which is the smallest step up from here.

---

## ADR 011 — The OAuth code flow by hand, with a signed, time-limited state

**Context.** Linking a GitHub account is the authorisation code flow. The subtlety is that the
provider redirects a *browser* to our callback, and a browser navigation carries no
`Authorization` header — so the request that completes the flow does not know who the user is.

**Options.**

| Option | Why not |
|---|---|
| **`AddAuthentication().AddOAuth("GitHub", …)`**, the built-in handler. | Designed to *sign a user in*, and it carries its correlation state in a cookie, which means adding a cookie scheme next to our bearer scheme for one feature. We are not signing in; we are storing a token for an already-authenticated user. The framework path fights the use case. |
| **A `state` table**: a random value, the user id and an expiry, inserted at `authorize` and looked up at `callback`. | Correct and conventional. But it is a migration, a table, a cleanup job for expired rows, and a lookup — state we would have to maintain forever for a value that lives ten minutes. |
| **The user id, plain, in `state`.** | Would "work" and is a serious vulnerability: anyone could craft a callback with someone else's user id and attach their GitHub account to that person's Dashboard. |
| **The user id sealed with a time-limited protector.** | **Chosen.** |

**Decision.** `GET /services/github/oauth/authorize` requires the JWT and returns `{ url }`; the SPA
navigates there. The `state` is the user id protected with `ToTimeLimitedDataProtector()` for 10
minutes. The callback is **anonymous on purpose**, unprotects the state to recover the user id, and
stores the resulting access token as an ordinary subscription to the `github` service.

**What it buys.** No table, no migration, no cleanup: the state is unforgeable without the server's
keys and expires by itself. One mechanism — Data Protection — covers both stored credentials and
the flow's state, with different purpose strings keeping them apart. Because the token is an
ordinary subscription, `DELETE /subscriptions/github` already unlinks the account, and the widget
layer needs no special case for OAuth services.

**What it costs.** We own the correctness of the flow, including the parts that are easy to get
wrong, and the invariants only live in tests and in this document. Two consequences look like bugs
and are not: typing the `authorize` URL into the address bar returns **401** (there is no JWT in a
navigation), and every failure after the configuration check is a **redirect** to
`{Frontend:Url}/services?github=failed&reason=…` rather than a problem document, because JSON in the
address bar is useless to a user. The route also cannot be tidied to `/oauth/github/…`: it must
match the callback URL registered on the GitHub OAuth app. And a state cannot be revoked once
issued — it is valid for its ten minutes, whatever happens.

**Alternative we would propose for a second iteration.** Bind the state to the browser that started the flow, as the OIDC sign-in already does ([ADR 016](#adr-016--sign-in-with-google-and-microsoft-oidc-by-hand)): a random value in both the sealed state and an `HttpOnly` cookie, compared at the callback. Without it, someone who sends you their own authorize link can have *your* GitHub account linked to *their* Dashboard user, if GitHub skips the consent screen for an app you already approved. Record each used state until it expires, so a state also works only once.

---

## ADR 012 — Relative API paths behind nginx, rather than a configured API URL

**Context.** The React app is built once into static files. Those files have to find the API, and
the API's address differs between `npm run dev`, Docker on a laptop, and anywhere it might be
deployed.

**Options.**

| Option | Why not |
|---|---|
| **`VITE_API_URL`, baked in at build time.** | Vite inlines environment variables into the bundle, so the address becomes part of the image. One image per environment, and a rebuild to change a hostname. |
| **Fetch the address at runtime** from a `config.json` next to the app. | Works, but it is an extra request before the app can do anything, plus a file to generate per environment. |
| **The browser calls the API's own origin** (`localhost:8080`) directly. | Means CORS, and a preflight on every mutating request. It also splits the origin: tokens in `localStorage` on `:8081` are invisible to anything served from `:8080`. |
| **Relative `/api/...` paths, rewritten by a proxy.** | **Chosen.** |

**Decision.** Every call in the front end is a relative `/api/...` path. In Docker, nginx serves
the SPA and rewrites `/api/` → `http://server:8080/`. In development, `vite.config.js` declares the
same rewrite. Nothing in the front end knows the API's hostname, and there is deliberately no
`VITE_API_URL`.

**What it buys.** One built image runs anywhere. The browser sees a single origin, so there is no
CORS configuration, no preflight, and no split `localStorage`. nginx also handles SPA routing —
`try_files … /index.html` — so a deep link like `/services` survives a page reload, and its config
already passes WebSocket upgrades through for a future push channel.

**What it costs.** Development and production rewrites are declared in two places that must agree —
`nginx.conf` and `vite.config.js` — and nothing checks that they do. The dev proxy points at
`localhost:8080`, the Dockerised API, **not** at `dotnet run` on `:5119`, which surprises everyone
once. And the front end cannot be served from a static host without a proxy in front of it; the
proxy is now part of the architecture rather than an optimisation.

**Alternative we would propose for a second iteration.** Keep relative paths, and make the two rewrites one fact. Either read the dev proxy target from an environment variable of the dev server only (it never reaches the bundle, unlike `VITE_API_URL`), or add a check in CI that `nginx.conf` and `vite.config.js` declare the same prefix.

---

## ADR 013 — Roles in the JWT, and an administrator bootstrapped from configuration

**Context.** The subject asks for an administration section. Someone has to be the first
administrator, and no one can grant it to them from inside the application.

**Options.**

| Option | Why not |
|---|---|
| **A seeded admin account** with a known email and password, created by a migration. | A default credential that exists in every clone, including a deployed one. The worst option, however convenient. |
| **A CLI command** (`dotnet run -- promote user@example.com`). | Safe, but requires shell access to the container and a second entry point to maintain. |
| **A flag on the register endpoint**, honoured for the first account. | "The first person to register becomes an administrator" is a race condition with a privilege escalation as its prize. |
| **An email address in configuration, promoted when that account exists.** | **Chosen.** |

**Decision.** `Admin:Email` names the administrator. Promotion runs on **two paths**, because that
account may register before or after the process starts: `AdminSeeder.SeedAsync` in the startup
scope, next to `Migrate()`, promotes an account that already exists; `AuthEndpoints.Register`
promotes one created later. Authorisation is a `role` claim per role in the JWT, a `RoleClaimType`
of `"role"`, and a named `admin` policy — `DefaultPolicy` is untouched, so every other
`RequireAuthorization()` behaves exactly as before. `AdminOptions` is bound **without**
`ValidateOnStart`, so a clone that configures no administrator still boots, with `/admin` simply
unreachable.

Two behaviours that look like mistakes are intentional. `/admin` returns **403**, not 404, to a
logged-in non-admin: the 404-not-403 rule elsewhere hides whether *someone else's row* exists, and a
role gate is not an ownership check. And **self-delete and self-revoke are refused** — blocking
self-revocation alone guarantees that no sequence of calls can ever reach zero administrators.

**What it buys.** No default credential anywhere. Promotion is gated by email ownership, since the
address still has to confirm its mailbox before it can log in ([ADR 008](#adr-008--email-confirmation-required-before-the-first-login)).
Authorisation costs no database round trip, because the roles travel in the token.

**What it costs.** Exactly because roles travel in the token, **a granted or revoked role only
reaches a user in a token minted afterwards: a revoked administrator keeps access until their
token expires**, up to 60 minutes. Fixing that needs server-side revocation — a denylist or very
short-lived tokens with refresh — which is out of scope here. The tests assert the behaviour as it
is rather than pretending otherwise. There is also no second mechanism: if the configured address
is wrong and no administrator exists, the fix is a configuration change and a restart.

**Alternative we would propose for a second iteration.** Validate Identity's security stamp: put it in the token as a claim, and compare it on each request against a short in-memory cache of the database value. Revoking a role, or deleting an account, updates the stamp and takes effect within seconds instead of up to 60 minutes, at the price of one cached lookup per request.

---

## ADR 014 — Integration tests against a real database, with stubbed providers

**Context.** Most of what can break here is in the wiring: authorisation filters, ownership scoping,
status codes, Identity's behaviour, EF Core's queries, the background service. Unit tests of
individual classes would not have caught any of the bugs we actually hit.

**Options.**

| Option | Why not |
|---|---|
| **Unit tests with mocks** for `UserManager`, `DbContext` and the clients. | Tests the code we wrote against our assumptions about the framework — which is where the bugs were. Mocking `DbContext` in particular tests LINQ against lists, not against SQL. |
| **EF Core's in-memory provider.** | Fast and tempting, but it is not a relational database: it ignores unique indexes and cascade deletes, which are two constraints we rely on. A test suite that passes against it can fail against Postgres. |
| **Testcontainers**, starting a throwaway Postgres per run. | The best isolation, and the direction we would go next. It also adds a dependency and a Docker-in-Docker concern for the containerised test run, so we deferred it. |
| **The real API in-process, against the real Postgres and Mailpit.** | **Chosen.** |

**Decision.** `ApiFactory` boots the actual application with `WebApplicationFactory<Program>`,
swapping only the database name to `dashboard_test`. 193 tests across 13 classes. External
providers are replaced by fake `HttpMessageHandler`s, so **the suite runs offline and must stay
that way**. `ApiFactoryWithStubs` adds stub `apiKey` and `oauth` services that join the registry
for those tests only — that is how the subscription layer is covered without a real credentialed
service. Every test creates its own user with a unique email, because the test database persists
between runs. `MailpitClient` reads confirmation links back out of the inbox, playing the part of
the user opening their mail. Timer tests poll `/data` until a status appears, exercising the real
background service.

**What it buys.** A test failure means the API's observable behaviour changed, which is the only
thing a consumer can depend on. Unique indexes, cascades, Identity's token validation and the
background loop are all genuinely exercised. The suite runs identically on a developer's machine
and inside the Compose network (`docker compose run --rm tests`), and needs no network.

**What it costs.** It needs Postgres and Mailpit running, so `dotnet test` on a clean machine fails
until `docker compose up -d db mailpit` — a real barrier for a newcomer, and the reason it is
spelled out in [backend.md](backend.md). The tests are slower than unit tests and share one
database, so they cannot run fully in parallel. State accumulates in `dashboard_test` across runs,
which is why uniqueness is a per-test concern. And every new provider needs a fake handler wired
into `ApiFactory`, or the suite starts reaching the internet — the one rule that keeps it
deterministic.

**Alternative we would propose for a second iteration.** Start Postgres and Mailpit from the test run itself with Testcontainers, so `dotnet test` works on a clean machine with only Docker installed. Give each test class its own database, so classes can run in parallel and no state accumulates between runs.

---

## ADR 015 — Every value from `IConfiguration`, never a literal

**Context.** The same code has to run with `dotnet run` on a laptop, inside Compose, and in the
test harness. Hostnames, ports, URLs, keys and the administrator's address all differ.

**Options.**

| Option | Why not |
|---|---|
| **Branch on the environment** (`if (env.IsDevelopment())`). | The branches multiply and the code describes deployment instead of behaviour. Worse, the production path is the one never exercised locally. |
| **A config file per environment**, selected at startup. | Partly what `appsettings.{Environment}.json` already is, but it does not cover values a container must inject, and it invites secrets into git. |
| **Everything through `IConfiguration`, overridden by environment variables.** | **Chosen.** |

**Decision.** Every value is read from configuration: `ConnectionStrings:Default`, `Jwt:*`,
`Email:*`, `Frontend:Url`, `DataProtection:KeysPath`, `GitHub:*`, `Admin:Email`. In Docker they
arrive as `Section__Key` environment variables, which ASP.NET maps to `Section:Key` — so the same
code works everywhere with no branching. `docker-compose.yml` gives every variable a default, so a
fresh clone runs with no `.env` at all; `.env`, which is git-ignored, only overrides them. Real
secrets live in `dotnet user-secrets` for development and in `.env` for Docker. `JwtOptions` and
`EmailOptions` are validated with `ValidateDataAnnotations().ValidateOnStart()`.

**What it buys.** No environment branching in the code. A grader can clone and `docker compose up`
with nothing configured. A missing or too-short signing key fails the boot with a message naming
the problem, instead of producing tokens nobody can verify. Secrets never need to be in a tracked
file.

**What it costs.** Compose defaults mean the stack **starts with a known development signing key
and a known database password if nobody overrides them** — convenient for grading, unacceptable for
deployment, and therefore stated as such in [security.md](security.md) and [deployment.md](deployment.md).
A secret has to be set twice for the two ways of running the project, because a container cannot
read `dotnet user-secrets`; forgetting the second is a familiar way to see a 503 from the GitHub
endpoints. And a typo in a key name is silent — configuration binding does not warn about a section
nobody reads.

**Alternative we would propose for a second iteration.** Refuse to boot outside `Development` when the known default JWT key or database password is in use: one startup check, so a forgotten `.env` fails loudly instead of running insecurely. Log configuration sections that are present but bound by nothing, so a typo in a key name stops being silent.

---

## ADR 016 — Sign in with Google and Microsoft, OIDC by hand

**Context.** Users should be able to sign in with an existing Google or Microsoft account instead of
a password. Our session is a JWT we mint ourselves ([ADR 007](#adr-007--a-signed-jwt-in-localstorage-rather-than-a-session-cookie)),
so whatever happens with the provider has to end in that same JWT.

**Options.**

| Option | Why not |
|---|---|
| **`AddAuthentication().AddOpenIdConnect(…)`**, the built-in handler. | It signs the user into a *cookie* scheme, then we would still have to turn that cookie into our JWT on a second hop. Two auth schemes for one feature, plus correlation and nonce cookies we do not control. |
| **A third-party identity broker** (Auth0, Keycloak). | A new service to run or to pay for, for two providers. |
| **The code flow by hand, with PKCE, a nonce, and a sealed state.** | **Chosen**, consistent with [ADR 011](#adr-011--the-oauth-code-flow-by-hand-with-a-signed-time-limited-state). |

**Decision.** `Auth/Oidc/` holds the provider catalogue in code (`OidcProviders`), named options per
provider, an `OidcClient` that reads each provider's discovery document through the
`Microsoft.IdentityModel.Protocols.OpenIdConnect` `ConfigurationManager` (already a dependency of
the JWT bearer handler), and `OidcSignIn`, which maps a validated `id_token` to a user. The
external identity is an `AspNetUserLogins` row, so there is **no migration**. The callback redirects to
the SPA with our JWT in a URL fragment.

Linking to an existing account by email follows ownership. A Google email links only if Google says
it is verified *and* our account confirmed it. A Microsoft email never links, because Entra lets a
tenant admin put any address on an account.

This **amends ADR 013**: `PromoteIfConfiguredAsync` now requires `EmailConfirmed`, and the
second promotion path moved from `Register` to `Confirm`. Before OIDC, an unconfirmed account could
not log in, so promoting it was harmless. An OIDC account with an unproved address *can* log in.

**What it buys.** One session mechanism for every way of signing in, no new package, no table, and
each security property — state, binding, PKCE, nonce, issuer, audience — visible in one file and
asserted by a test.

**What it costs.** We own an authentication protocol, the part where mistakes are most expensive. The
provider rules (Google's two issuer spellings, Microsoft's `{tenantid}` issuer template, which claims
carry an email) are ours to keep current. Microsoft users whose address is already registered cannot
sign in with Microsoft until account linking from a logged-in session exists, which is out of scope.

**Alternative we would propose for a second iteration.** Add account linking from a logged-in session: an authenticated variant of `/authorize` that attaches the provider identity to the current user. That fixes the case of a Microsoft user whose address already has a password account. For Microsoft, also request the optional `xms_edov` claim ("email domain owner verified"), and treat the email as verified when Microsoft asserts it.

