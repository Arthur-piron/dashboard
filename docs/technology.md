# Technology choices

Everything this project depends on, why it is here, and what we rejected. The *design* decisions
behind the code live in [decisions.md](decisions.md); this document is about the stack itself.

Three rules shaped the whole list:

1. **A fresh clone must run with one command.** Nothing may require an account, an API key or a
   cloud service to start. That alone rules out hosted databases, mail providers and secret
   managers.
2. **Prefer what the platform already ships.** Two students, a few weeks. Every dependency added
   is a thing to learn, update and defend; every dependency avoided is time spent on the subject
   instead.
3. **The same code in every environment.** Differences belong in configuration, not in branches
   ([ADR 015](decisions.md#adr-015--every-value-from-iconfiguration-never-a-literal)).

## The stack at a glance

| Layer | Choice | Version | In one line |
|---|---|---|---|
| Back end | .NET / ASP.NET Core, minimal API | 10.0 | Identity, scheduling, encryption and integration tests are first-party ([ADR 001](decisions.md#adr-001--net-10-minimal-api-for-the-back-end)) |
| Database | PostgreSQL | 17 (alpine) | relational data with constraints we want enforced ([ADR 003](decisions.md#adr-003--postgresql-with-ef-core-migrations)) |
| ORM | EF Core + Npgsql | 10.0 | code-first migrations, and the store Identity already uses |
| Identity | ASP.NET Identity Core | 10.0 | password hashing, confirmation tokens, roles — not hand-rolled ([ADR 006](decisions.md#adr-006--aspnet-identity-core-without-its-ui)) |
| Tokens | JWT bearer, HS256 | 10.0 | stateless auth, no session store ([ADR 007](decisions.md#adr-007--a-signed-jwt-in-localstorage-rather-than-a-session-cookie)) |
| Encryption at rest | ASP.NET Data Protection | built in | protects third-party credentials and the OAuth state ([ADR 010](decisions.md#adr-010--third-party-credentials-encrypted-with-aspnet-data-protection)) |
| Mail | MailKit → Mailpit | 4.18 / latest | real SMTP, local inbox, no account needed ([ADR 008](decisions.md#adr-008--email-confirmation-required-before-the-first-login)) |
| Background work | `BackgroundService` + `PeriodicTimer` | built in | one loop owns every provider call ([ADR 009](decisions.md#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache)) |
| Front end | React | 19.2 | the group's shared experience ([ADR 002](decisions.md#adr-002--react--vite-in-plain-javascript-no-typescript)) |
| Build tool | Vite | 8.3 | instant dev server, static output nginx can serve |
| Language (front) | plain JavaScript, `.jsx` | — | deliberately no TypeScript, with a cost we name ([ADR 002](decisions.md#adr-002--react--vite-in-plain-javascript-no-typescript)) |
| Routing | react-router-dom | 7.18 | the only front-end runtime dependency beyond React |
| Styling | plain CSS, one file per component | — | design tokens as CSS variables, no Tailwind, no UI kit |
| Web server | nginx | 1.27 (alpine) | serves the SPA and proxies `/api/` ([ADR 012](decisions.md#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url)) |
| Containers | Docker + Compose | — | the delivery is `docker compose build && up` ([deployment.md](deployment.md)) |
| Tests | xUnit + `WebApplicationFactory` | 2.9 / 10.0 | 193 integration tests against the real API ([ADR 014](decisions.md#adr-014--integration-tests-against-a-real-database-with-stubbed-providers)) |

The front end's entire runtime dependency list is `react`, `react-dom`, `react-router-dom`. The back
end's is five packages: MailKit, the JWT bearer handler, Identity's EF Core store, Npgsql's EF Core
provider, and EF Core's design-time tools.

## Why .NET rather than Node

This is the choice a reader is most likely to question, so here it is in full.

The honest starting point: **Node and Express would have been faster for us to begin.** We knew
them better. What changed our mind was listing what this subject actually requires and asking, for
each item, who writes it.

| Requirement | With .NET | With Node + Express |
|---|---|---|
| Password hashing, uniqueness, lockout | `UserManager` | a hashing library, chosen and configured by us |
| Email confirmation tokens | Identity token providers | our own token, signed and expired by us |
| Roles and authorisation policies | `AddRoles` + policies | our own middleware |
| Encryption of stored credentials | Data Protection, with key management | a crypto library, plus our own key handling |
| Scheduled background refresh | `BackgroundService` | a scheduler library, or `setInterval` and care |
| Config binding with startup validation | Options + data annotations | a schema library, wired by us |
| In-process API tests | `WebApplicationFactory` | supertest plus a server lifecycle we manage |

Seven pieces of security-sensitive or lifecycle-sensitive plumbing, versioned and documented
together, against seven independent choices to make and defend. For a project whose interesting
work is the widget catalogue and the refresh timer, that settled it.

**What we gave up:** the ecosystem familiarity of the rest of the promotion, a much smaller
container image, and sharing one language across both halves of the project. The first cost was
real — when the back end was stuck, there was nobody nearby to ask.

## Comparisons, layer by layer

Each table compares the options we actually weighed for one layer, on the criteria that decided it.
Most criteria come from the three rules above. The verdicts are our own assessment for *this*
project, a two-person app run from one `docker compose up`, not a general ranking. The full
reasoning, with the costs we accepted, is in the ADR linked under each table.

### Back-end framework

| | **ASP.NET Core 10** (chosen) | Express (Node) | NestJS (Node) | Django (Python) | Spring Boot (Java) |
|---|---|---|---|---|---|
| Accounts, password hashing, confirmation tokens, roles | built in (Identity) | libraries we pick and wire | libraries, with Passport | built in (`django.contrib.auth`) | Spring Security, heavy to configure |
| Encrypting stored third-party credentials | built in (Data Protection, with key rotation) | a crypto library + our own key handling | same as Express | a library, or our own | a library, or our own |
| A scheduled background refresh in the same process | built in (`BackgroundService`) | `setInterval` or a scheduler library | `@nestjs/schedule` | Celery + a broker, a second process | `@Scheduled`, built in |
| In-process integration tests against the real app | built in (`WebApplicationFactory`) | supertest, server lifecycle ours | built in (`TestingModule`) | built in test client | built in (`@SpringBootTest`) |
| Team familiarity at the start | low | **high** | medium | low | low |
| Container image | 363 MB, our built `dashboard-server` | from 234 MB, the `node:24-alpine` base alone | from 234 MB, same base | not measured | not measured |

**Verdict:** Express would have been the fastest start, but it would have meant writing or choosing
about seven security- and lifecycle-sensitive pieces ourselves. ASP.NET Core ships all of them,
versioned together. Detail in "Why .NET rather than Node" above and
[ADR 001](decisions.md#adr-001--net-10-minimal-api-for-the-back-end).

### Front-end framework

| | **React 19 + Vite** (chosen) | Vue 3 + Vite | Angular | Svelte(Kit) | Next.js |
|---|---|---|---|---|---|
| Fits a static build served by nginx | yes | yes | yes | yes | needs a Node server for its main features |
| Framework to learn for a dozen components | small | small | large (modules, DI, RxJS) | small | medium (server and client components) |
| Team familiarity | **high** | low | low | none | medium |
| Runtime dependencies we ship | 3 (`react`, `react-dom`, `react-router-dom`) | 2–3 | many | 1–2 | many |
| Features we would not use | none | none | most of the framework | none | server rendering, SEO, API routes |

**Verdict:** Vue or Svelte would have worked just as well technically. React won on the group's
familiarity, and we record that as a preference rather than a technical superiority.
[ADR 002](decisions.md#adr-002--react--vite-in-plain-javascript-no-typescript).

### Database

| | **PostgreSQL 17** (chosen) | MySQL / MariaDB | MongoDB | SQLite |
|---|---|---|---|---|
| Enforces *(user, service)* uniqueness and cascading deletes | yes | yes | in application code | yes |
| Store for ASP.NET Identity | official EF Core provider (Npgsql) | community provider | no maintained one | yes |
| Runs as its own container, shared by API and tests | yes | yes | yes | a file, awkward across containers |
| Same engine in development, tests and production | yes | yes | yes | rarely used in production |
| JSON column if we ever need it | `jsonb`, indexable | `JSON` | native | text |

**Verdict:** the data is small and strictly relational (users own subscriptions, which own
instances), so a relational engine with real constraints. PostgreSQL over MySQL for the
first-party EF Core provider and `jsonb`.
[ADR 003](decisions.md#adr-003--postgresql-with-ef-core-migrations).

### Sessions and sign-in

| | **JWT bearer in `localStorage`** (chosen) | Session cookie + server store | `httpOnly` cookie + short JWT + refresh token | Hosted identity (Auth0, Keycloak) |
|---|---|---|---|---|
| State on the server | none | a session table or Redis | a refresh-token table | an external service |
| CSRF protection to build | none (a header is not sent automatically) | on every mutating request | on every mutating request | depends |
| Token readable by an XSS | **yes**, our named risk | no | no | depends |
| Server-side logout and revocation | no (expires after 60 min) | yes | yes | yes |
| Extra moving parts | none | store | 2 endpoints + rotation | a whole service, an account |

**Verdict:** stateless and the simplest to defend, at a cost we state openly: an XSS could read the
token. The `httpOnly` + refresh variant is what we would build next.
[ADR 007](decisions.md#adr-007--a-signed-jwt-in-localstorage-rather-than-a-session-cookie). Google
and Microsoft sign-in end in the same JWT
([ADR 016](decisions.md#adr-016--sign-in-with-google-and-microsoft-oidc-by-hand)).

### Refreshing widgets

| | **One in-process timer + cache** (chosen) | The browser calls providers | The API fetches on each request | Hangfire / Quartz | A separate worker + Redis |
|---|---|---|---|---|---|
| Provider calls independent of open tabs | yes | no, one call per tab | no, one call per poll | yes | yes |
| Secrets stay on the server | yes | **no**, keys in the browser | yes | yes | yes |
| Survives a restart | rebuilt from the database at boot | n/a | n/a | persisted jobs | persisted |
| Extra infrastructure | none | none | none | a job store | a worker + Redis |

**Verdict:** the timer owns every provider call, so polling rate and provider call rate are
independent. Persistence of jobs buys nothing when a job is "call one API, keep the answer".
[ADR 009](decisions.md#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache).

### Serving the app and reaching the API

| | **nginx + relative `/api/` paths** (chosen) | `VITE_API_URL` baked at build | Runtime `config.json` | Browser calls `:8080` directly (CORS) |
|---|---|---|---|---|
| One image for every environment | yes | no, one build per address | yes | yes |
| CORS and preflight requests | none, one origin | none | none | on every mutating call |
| SPA routes like `/services` on reload | nginx `try_files` | needs a server too | needs a server too | needs a server too |

**Verdict:** one built image runs anywhere, and the browser sees a single origin.
[ADR 012](decisions.md#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url).

### Packaging and deployment

| | **Docker Compose** (chosen) | Plain `docker run` scripts | Kubernetes | Install on the host |
|---|---|---|---|---|
| One command from a fresh clone | yes, `docker compose up` | a script per service | needs a cluster | many manual steps |
| Service names as DNS between containers | yes | a network we create | yes | no |
| Required by the subject | **yes** | no | no | no |
| Fit for two students | yes | yes | far too much | fragile across machines |

**Verdict:** the subject asks for `docker-compose build` / `up`, and Compose is also the simplest
tool that runs four services together. [deployment.md](deployment.md).

## Why these five providers

A service is only worth building if a grader can see it work. That ruled out anything requiring a
paid plan or an approval delay.

| Service | Provider | Authentication | Why it is in the catalogue |
|---|---|---|---|
| Weather | Open-Meteo | none | no key at all, and a separate geocoding endpoint, so "a city name" becomes two calls — a realistic client |
| Crypto | CoinGecko | none | its free tier answers an unknown coin with `200` and `{}`, which forced us to handle a provider that lies with its status code |
| Duolingo | Duolingo's public profile endpoint | none | an undocumented endpoint, which is a useful contrast: no contract, so the client is defensive by necessity |
| NASA | api.nasa.gov | API key | the one service that exercises the whole credential path, and a personal key is free and arrives by email at once |
| GitHub | GitHub REST API | OAuth | the one service that exercises the OAuth flow, and the only one where the credential is a token we obtain rather than one the user pastes. Its widgets stay on the `read:user` scope, so they read only public data |

The spread is deliberate: the three `AuthKind` values — `None`, `ApiKey`, `Oauth` — are each covered
by a real service, so no branch of the subscription code is theoretical.

## What we deliberately did not add

| Not used | Why |
|---|---|
| **TypeScript** (front end) | A conscious trade recorded in [ADR 002](decisions.md#adr-002--react--vite-in-plain-javascript-no-typescript), with its cost named: nothing checks that the front end reads the shape the API returns. We would reintroduce it at the API boundary first. |
| **Tailwind / a UI kit** | The design is a Figma file translated by hand into design tokens. A utility framework or a component library would have imposed its own tokens on a mockup we had to match exactly. |
| **Redux / Zustand / Context** | The only shared state is the theme. One `useState` in a hook, passed down two levels. |
| **A charting library** | Two widgets draw a chart (the crypto price history and the GitHub commits per day), each as a few lines of hand-written SVG. A library would weigh more than both charts together. |
| **An ORM-less data layer (Dapper)** | Identity ships an EF Core store; using Dapper would have meant writing the identity schema and its queries ourselves. |
| **Hangfire / Quartz** | Durable distributed jobs for work that is "call one API, keep the answer in memory". See [ADR 009](decisions.md#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache). |
| **Redis** | Nothing needs a shared cache while the API is single-instance — and the timer's in-memory cache is exactly what would become Redis if that changed. |
| **A secret manager** (Vault, KMS) | It is the right production answer and it is an external dependency with credentials of its own. Incompatible with "runs from a fresh clone". See [security.md](security.md). |
| **SignalR** | Push would remove the front end's polling, and the nginx config already passes WebSocket upgrades through for it. It is real future work, not a rejected option. |
| **A cloud database or mail provider** | Both break rule 1: nothing may require an account to start. |

## Tooling

| Tool | For |
|---|---|
| `dotnet-ef` (global tool) | adding and removing migrations |
| `dotnet user-secrets` | development secrets, outside the repository |
| ESLint (+ the React hooks and refresh plugins) | linting the front end |
| Mailpit's web UI on `:8025` | reading the mail the application sends, in development and in Docker |
| `bonus/Makefile` | shortcuts over the Compose commands (`build`, `up`, `bup`, `down`, `test`, `test-local`, `dev`, `logs`, `clean`) |
| `bonus/test-github-oauth.sh` | drives the GitHub OAuth flow end to end against a running API |

## Versions and upgrades

Back-end package versions are pinned exactly in the `.csproj` files. Front-end versions use caret
ranges, which is npm's default, and `package-lock.json` is committed — so `npm ci` in the Dockerfile
installs the exact tree we tested, while `npm install` locally may pick up patch releases.

Both base images are pinned to a major version (`postgres:17-alpine`, `nginx:1.27-alpine`,
`node:24-alpine`, `dotnet/aspnet:10.0`). Mailpit is the one image tracking `latest`, which is
acceptable only because it is a development dependency that never runs in production.
