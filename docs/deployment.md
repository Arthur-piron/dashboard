# Containerisation and deployment

Everything about running this project: the images, the Compose stack, the volumes that matter, and
what would have to change before it could face the internet.

## The delivery command

```bash
docker compose build
docker compose up
```

From a fresh clone, with no `.env` and nothing installed but Docker. Four containers start, the
database creates itself, the API migrates its own schema, and the app is on
<http://localhost:8081>.

| Published port | Container | What |
|---|---|---|
| `8081` | `client` | the React app, served by nginx — **this is the application** |
| `8080` | `server` | the API directly, for `GET /about.json` and for tools like Postman |
| `8025` | `mailpit` | the web inbox where confirmation emails land |
| `1025` | `mailpit` | SMTP, which the API uses |
| `5432` | `db` | PostgreSQL, exposed so a developer can connect with `psql` or run tests on the host |

Everything else talks over the Compose network by service name: nginx reaches `server:8080`, the
API reaches `db:5432` and `mailpit:1025`.

## The images

Both application images are **multi-stage**: a fat build stage, then a small runtime stage that
copies only the output. The build tools never ship.

### `DashboardBackEnd/Dockerfile`

```
sdk:10.0   ──▶  copy the .csproj  ──▶  dotnet restore
                copy the sources  ──▶  dotnet publish -c Release -o /app
                                            │
aspnet:10.0 ◀───────────────────────────────┘  copy /app only
                mkdir /keys, chown to $APP_UID, USER $APP_UID
                ASPNETCORE_URLS=http://+:8080
```

Two details are load-bearing:

- **The `.csproj` is copied before the sources, on its own layer.** `dotnet restore` then sits in a
  layer that is reused as long as the project file is unchanged, so editing a `.cs` file does not
  re-download every NuGet package. The same pattern appears in the front-end image with
  `package*.json`.
- **The container runs as a non-root user.** The `aspnet` image ships an `app` user as `$APP_UID`;
  `/keys` is created and chowned to it *before* the `USER` switch, because a non-root process cannot
  create a directory at the root of the filesystem. Get the order wrong and the container starts and
  then fails to write its Data Protection keys.

### `DashboardFrontEnd/Dockerfile`

```
node:24-alpine  ──▶  copy package*.json  ──▶  npm ci
                     copy the sources    ──▶  npm run build  →  /app/dist
                                                   │
nginx:1.27-alpine ◀────────────────────────────────┘  copy dist to /usr/share/nginx/html
                     copy nginx.conf to /etc/nginx/conf.d/default.conf
```

`npm ci` installs exactly the tree in `package-lock.json` rather than resolving ranges afresh, so
the image contains the dependencies we tested. The final image holds static files and nginx — no
Node, no `node_modules`.

Both builds use the **repository root as context** (`context: .` with a `dockerfile:` path), because
the back-end image needs two project files from different folders. `.dockerignore` keeps `bin/`,
`obj/`, `node_modules/`, `dist/`, `.git/` and `.env` out of the build context — without it the
context would be hundreds of megabytes and a stale local build could leak into the image.

### `DashboardBackEnd.Tests/Dockerfile`

A single-stage SDK image whose entry point is `dotnet test`. It exists so the suite can run *inside*
the Compose network, where `db` and `mailpit` resolve by name:

```bash
docker compose run --rm tests        # profile `test`
```

It sits behind `profiles: [test]`, so `docker compose up` ignores it.

## The Compose stack

```
client ──depends_on──▶ server ──depends_on (healthy)──▶ db
                              ──depends_on────────────▶ mailpit
tests  ──depends_on──▶ db, mailpit          [profile: test]
```

`db` has a **health check** (`pg_isready`, every 5s, 10 retries) and `server` waits on
`condition: service_healthy`. Without it the API would start first, fail to connect, and crash —
`depends_on` alone only waits for the container to exist, not for Postgres to accept connections.

`client` depends on `server` only for start order; nginx does not need the API to be up, because it
resolves `server` lazily per request.

### Volumes

| Volume | Mounted at | Holds |
|---|---|---|
| `postgres-data` | `/var/lib/postgresql/data` | every account, subscription and widget instance |
| `dataprotection-keys` | `/keys` | the Data Protection key ring |

**Both must be backed up together.** The key ring encrypts the credentials stored in the database:
losing `/keys` while keeping the database leaves every stored API key and OAuth token permanently
unreadable, and users would have to re-enter them. This is the single most important operational
fact about this stack — see [security.md](security.md#the-key-ring-is-critical-state).

`docker compose down` keeps both volumes. `docker compose down -v` (and `make clean`) deletes them,
which is how you reset to an empty installation.

### Configuration

Every setting reaches the API as a `Section__Key` environment variable, which ASP.NET maps to
`Section:Key` — so the same code runs in Docker and on a laptop with no branching
([ADR 015](decisions.md#adr-015--every-value-from-iconfiguration-never-a-literal)).

Every variable in `docker-compose.yml` has a **default**, which is what makes `docker compose up`
work on a fresh clone with no `.env`. Copy `.env.example` to `.env` to override any of them:

| Variable | Default | Notes |
|---|---|---|
| `POSTGRES_PASSWORD` | `dashboard` | shared by `db` and the API's connection string |
| `JWT_KEY` | a placeholder in `.env.example` | must be ≥ 32 characters, or the API refuses to start |
| `FRONTEND_URL` | `http://localhost:8081` | the base of confirmation links and OAuth redirects |
| `ASPNETCORE_ENVIRONMENT` | `Development` | |
| `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` | empty | the OAuth endpoints answer 503 while unset |
| `GITHUB_CALLBACK_URL` | `http://localhost:8081/api/services/github/oauth/callback` | must match the GitHub OAuth app exactly |
| `ADMIN_EMAIL` | empty | the account promoted to administrator |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | empty | "Continue with Google" is hidden while unset |
| `GOOGLE_CALLBACK_URL` | `http://localhost:8081/api/auth/oidc/google/callback` | must be an authorized redirect URI of the Google OAuth client |
| `MICROSOFT_CLIENT_ID` / `MICROSOFT_CLIENT_SECRET` | empty | "Continue with Microsoft" is hidden while unset |
| `MICROSOFT_CALLBACK_URL` | `http://localhost:8081/api/auth/oidc/microsoft/callback` | must be a Web redirect URI of the Entra app registration |
| `MICROSOFT_TENANT` | Epitech's tenant id (`901cb4ca-b862-4029-9306-e5cd0f6d9f86`) | must match the app registration's *supported account types*: our registration is Epitech-only. `common` (any work/school or personal account), `organizations` or `consumers` work only once the registration allows them, otherwise Microsoft answers **AADSTS50194** |

`.env` is git-ignored. It is also excluded from the build context, so a secret in it cannot end up
baked into an image.

## nginx: one origin for the browser

`DashboardFrontEnd/nginx.conf` does two jobs.

```nginx
location / {
    try_files $uri $uri/ /index.html;    # SPA routing: /services survives a reload
}

location /api/ {
    proxy_pass http://server:8080/;      # the trailing slash strips the /api prefix
    ...
}
```

The trailing slash on `proxy_pass` is the whole trick: `/api/about.json` becomes
`http://server:8080/about.json`. Remove it and every call 404s with the prefix still attached.

Because the browser only ever sees `:8081`, there is no CORS configuration anywhere in this project
and no preflight on any request ([ADR 012](decisions.md#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url)).
The config also forwards `X-Forwarded-For` / `X-Forwarded-Proto` — the API reads them through
`UseForwardedHeaders`, which is what makes a redirect it builds come out with the right scheme — and
passes WebSocket upgrades through, ready for a future push channel.

## Day-to-day development

Running the whole stack in Docker for every code change is slow. The usual loop puts the
dependencies in containers and the code on the host:

```bash
docker compose up -d db mailpit          # Postgres :5432, Mailpit :8025 / :1025
dotnet run --project DashboardBackEnd    # API on http://localhost:5119
cd DashboardFrontEnd && npm run dev      # Vite on http://localhost:5173
```

One trap: **Vite's dev proxy targets `localhost:8080`, the Dockerised API — not `dotnet run` on
`:5119`.** So `npm run dev` talks to the container, and `dotnet run` is for Postman and the tests. If
you want the dev server to hit your local build, change the target in `vite.config.js`.

The other trap, and the one that costs the most time:

> **After changing back-end code, `docker compose up` alone serves a stale image.** It does not
> rebuild. Use `docker compose up --build` (or `make bup`).

A quick check that the running image is current: `curl localhost:8080/about.json` lists exactly the
services compiled into it.

### `bonus/Makefile`

| Target | Does |
|---|---|
| `make build` / `make up` | `docker compose build` / `up` |
| `make bup` | down, then `up --build -d` — the one to use after a back-end change |
| `make dev` | just `db` and `mailpit`, for the host loop above |
| `make test` | the suite inside the Compose network |
| `make test-local` | starts `db` and `mailpit`, then `dotnet test` on the host |
| `make logs` | follows the API's logs |
| `make down` / `make clean` | stop; stop **and delete the volumes** |

## Verifying a delivery

```bash
docker compose down -v          # start from nothing
docker compose build
docker compose up
```

Then check, in order:

1. **`curl localhost:8080/about.json`** returns `client.host`, `server.current_time`, and
   `server.services[]` where every widget lists its parameters with a type of `integer` or `string`.
   This also proves which services are compiled into the running image.
2. **<http://localhost:8081>** serves the app, and a deep link like
   <http://localhost:8081/services> survives a reload (nginx's `try_files`).
3. **Register an account**, find the mail in <http://localhost:8025>, confirm, and log in.
4. **`docker compose run --rm tests`** — 193 tests, green, with no network access.
5. **`docker compose down && docker compose up`** — the account from step 3 is still there, because
   the volumes survived.

## What is missing for production

This stack is built to be graded, not deployed. Honestly:

| Gap | Why it matters | Smallest fix |
|---|---|---|
| **Compose defaults are known secrets** | `JWT_KEY` and `POSTGRES_PASSWORD` have working defaults, so an unconfigured deployment runs on credentials that are in the repository. | Require them: drop the defaults so an unset variable fails the boot. |
| **No TLS** | Tokens and passwords cross the network in clear. | A reverse proxy terminating HTTPS in front of `client` (Caddy, Traefik, or nginx with certbot). |
| **`ASPNETCORE_ENVIRONMENT=Development`** | Verbose errors, developer exception pages. | Set `Production`. |
| **Mailpit is not a mail server** | It catches mail; it does not deliver it. Nobody receives a confirmation link. | Point `Email:*` at a real SMTP service. |
| **Migrations run at startup** | Two replicas booting together would race on the schema. | Run `dotnet ef database update` as a deployment step. |
| **The API cannot be scaled** | The refresh timer's schedule and cache are in memory, so every replica would refresh every widget ([ADR 009](decisions.md#adr-009--one-background-timer-owns-every-fetch-endpoints-only-read-a-cache)). | Move the cache to Redis and elect one scheduler, or keep the timer in a single worker. |
| **The database is a container** | A single container with a local volume is not a durable database. | A managed Postgres, or at least backups of `postgres-data` **and** `dataprotection-keys`. |
| **No key-ring protection** | The Data Protection keys sit as plain files on a volume. | Protect the key ring with a certificate or a cloud KMS. |
| **No CI** | Nothing runs the 193 tests automatically. | A workflow running `docker compose run --rm tests` on every pull request. |
