# Security and secrets

What this application protects, how, and where it knowingly falls short. Read
[architecture.md](architecture.md) first if you have not: the split between authenticating *our*
users and holding credentials for *third parties* runs through everything below.

## Secrets management

### The rule

> **No secret is ever written in source code, and no secret is ever committed.**

Every value comes from `IConfiguration`. Nothing is a literal
([ADR 015](decisions.md#adr-015--every-value-from-iconfiguration-never-a-literal)).

### Where each secret lives

| Secret | Development (`dotnet run`) | Docker | Committed? |
|---|---|---|---|
| `Jwt:Key` | `appsettings.json` — a placeholder marked not-for-production | `JWT_KEY` in `.env` | the dev placeholder is, a real key never |
| `ConnectionStrings:Default` (contains the password) | `appsettings.json`, local Postgres | built from `POSTGRES_PASSWORD` in `.env` | the local dev string is |
| `GitHub:ClientSecret` | **`dotnet user-secrets`** | `GITHUB_CLIENT_SECRET` in `.env` | **never** |
| `GitHub:ClientId` | `appsettings.json` | `.env` | yes — a client id is public by design; it appears in the URL the user's browser visits |
| `Oidc:Google:ClientSecret`, `Oidc:Microsoft:ClientSecret` | **`dotnet user-secrets`** | `GOOGLE_CLIENT_SECRET`, `MICROSOFT_CLIENT_SECRET` in `.env` | **never** |
| `Oidc:*:ClientId` | `dotnet user-secrets` or `appsettings.Development.json` | `.env` | may be — public by design, like GitHub's |
| `Admin:Email` | `appsettings.json` (empty) | `ADMIN_EMAIL` in `.env` | not a secret, but deployment-specific |
| The Data Protection key ring | `DataProtection:KeysPath` on disk | the `/keys` volume | **never** — it is generated, not configured |
| A user's NASA API key, a user's GitHub token | the database, encrypted | the database, encrypted | never leaves the server |

Three mechanisms, each for a reason:

- **`dotnet user-secrets`** keeps development secrets in the user's profile
  (`~/.microsoft/usersecrets/<id>/`), outside the repository, keyed by the `UserSecretsId` in the
  `.csproj`. It cannot be committed by accident.
  ```bash
  dotnet user-secrets set "GitHub:ClientSecret" "<value>" --project DashboardBackEnd
  ```
- **`.env`** carries the same values into Docker. It is in `.gitignore` *and* in `.dockerignore`, so
  it can neither be committed nor baked into an image. `.env.example` documents the variables with
  placeholder values and is committed.
- **Environment variables** are how Compose passes them to the container, as `Section__Key`.

A container cannot read `dotnet user-secrets`, which is why a secret has to be set in both places if
you use both ways of running the project. Forgetting the second is the usual reason the GitHub
endpoints answer 503.

### What *is* committed, and why that is acceptable

`appsettings.json` contains a development JWT key and a local database password. Both are
placeholders for a database that exists only on a developer's machine, and the key is named
`dev-only-jwt-signing-key-not-for-production-use-…` so that it cannot be mistaken for a real one.

The honest risk is the other side of that convenience: **`docker-compose.yml` gives every variable a
default, so an unconfigured stack boots with a signing key and a database password that are in the
repository.** That is deliberate — a grader must be able to clone and `docker compose up` — and it is
unacceptable for a deployment. The fix is to drop the defaults so an unset variable fails the boot;
it is listed in [deployment.md](deployment.md#what-is-missing-for-production).

A signing key is also validated, not merely read: `JwtOptions` requires at least 32 characters
(256 bits for HS256) and is bound with `ValidateDataAnnotations().ValidateOnStart()`, so a short or
missing key stops the application at boot with a message naming the problem — rather than producing
tokens nobody can verify.

### If a secret leaks

| Leaked | Consequence | What to do |
|---|---|---|
| `Jwt:Key` | anyone can mint a token for any user, including an administrator | change it; every existing token becomes invalid, which logs everyone out |
| `GitHub:ClientSecret` | the OAuth app can be impersonated | rotate it on GitHub, update `.env` and user-secrets |
| `POSTGRES_PASSWORD` | database access — but stored credentials stay encrypted | rotate it; restart the stack |
| the Data Protection key ring | every stored API key and OAuth token becomes readable | rotate the key ring, then have every user re-enter their credentials and re-link GitHub |

## Authenticating our users

```
register ──▶ 202 + an email ──▶ confirm ──▶ login ──▶ JWT (60 min) ──▶ Bearer on every call
```

- **Passwords** are hashed by ASP.NET Identity. We never implemented hashing, comparison or lockout
  ([ADR 006](decisions.md#adr-006--aspnet-identity-core-without-its-ui)). Policy: at least 8
  characters, and Identity's defaults still require an upper case letter, a lower case letter and a
  digit. Non-alphanumerics are not required.
- **Email confirmation is mandatory before the first login.** Logging in before confirming returns
  **403** with `type: "email_not_confirmed"` — expected behaviour, not a bug. The confirmation token
  is Identity's own, URL-safe base64 encoded for the link. This is also what gates promotion to
  administrator ([ADR 008](decisions.md#adr-008--email-confirmation-required-before-the-first-login)).
- **The token** is HS256, signed with `Jwt:Key`, carrying `sub`, `name`, `email` and one `role` claim
  per role. `MapInboundClaims = false` keeps the short claim names, and `RoleClaimType = "role"` is
  what makes `RequireRole` read them. It is validated on issuer, audience and signature.
- **A failed login is indistinguishable** from an unknown email: both return `401` with no body, so
  the endpoint does not reveal which addresses are registered.

### The known weakness: the token is readable by scripts

The JWT lives in `localStorage`, so any script running on our origin can read it. We chose this over
an `httpOnly` cookie, with the trade-off written down in full in
[ADR 007](decisions.md#adr-007--a-signed-jwt-in-localstorage-rather-than-a-session-cookie). Two
consequences to state plainly:

- An XSS flaw anywhere in the front end is an account takeover, where `httpOnly` would have limited
  it to actions performed in the victim's session.
- **There is no server-side logout.** Logging out deletes the local copy; a token already copied
  elsewhere stays valid until it expires.

In exchange there is no CSRF surface at all, because a cross-site page cannot make the browser
attach an `Authorization` header the way it attaches a cookie.

## Protecting third-party credentials

A user's NASA API key and GitHub access token are credentials at *someone else's* service. We must
be able to read them back to make calls, so hashing is not an option — they are **encrypted**
([ADR 010](decisions.md#adr-010--third-party-credentials-encrypted-with-aspnet-data-protection)).

`CredentialProtector` serialises a `ServiceCredentials` record and protects it with a
purpose-scoped protector; `ServiceSubscriptions.ProtectedCredentials` holds the ciphertext. The
purpose string (`DashboardBackEnd.ServiceCredentials.v1`) means a credential blob cannot be replayed
as an OAuth state, and vice versa.

Three invariants, all asserted by tests:

1. **No endpoint ever returns a credential.** `GET /subscriptions` returns the service key and
   timestamps — never the key or the token.
2. **The stored column really holds ciphertext.** A test reads the raw column and asserts the
   plaintext is not in it.
3. **A credential that cannot be decrypted degrades, it does not crash.** `Unprotect` returns `null`
   on a cryptographic or JSON failure, which surfaces as a widget error rather than a 500.

An API key is also validated *before* it is stored: `ValidateCredentialsAsync` asks the provider, so
a typo is rejected at subscription time instead of becoming a permanently failing widget.

### The key ring is critical state

The Data Protection keys are generated by the framework and persisted to `DataProtection:KeysPath` —
the `/keys` volume in Compose.

> **Losing those keys makes every stored credential permanently unreadable, even with a perfectly
> intact database.** Back up `dataprotection-keys` together with `postgres-data`, or treat a restore
> as "every user must re-enter their API keys and re-link GitHub".

The keys are only as protected as the filesystem holding them: there is no envelope encryption and no
audit of which process read them. Protecting the key ring with a certificate or a cloud KMS is the
smallest step up, and is listed as production work.

## Authorisation

### Ownership is enforced in the query

Every instance and subscription lookup filters by the JWT's user id **in the same `WHERE` clause**,
never by fetching a row and then comparing:

```csharp
db.WidgetInstances.SingleOrDefaultAsync(w => w.Id == id && w.UserId == principal.UserId(), ct)
```

Someone else's row therefore returns **404, not 403** — the existence of an id is never revealed.
A `DELETE` of a row you do not own is a no-op that reports 404 for the same reason.

### Why `/admin` answers 403 where everything else answers 404

The 404-not-403 rule hides whether *someone else's row* exists from a user with no business knowing.
An administrator legitimately may know which ids exist, and a **role gate is not an ownership
check** — so `/admin/users` returns **403** to a logged-in non-admin. Treating the two cases
identically would mean either leaking row existence or pretending the admin section does not exist,
and neither is more secure.

### Bootstrapping and keeping an administrator

The first administrator is named by `Admin:Email` and promoted on two paths — at startup, and when
an account created later confirms its address. There is **no default administrator account and no
default password anywhere** ([ADR 013](decisions.md#adr-013--roles-in-the-jwt-and-an-administrator-bootstrapped-from-configuration)).
Promotion is gated by `EmailConfirmed`, checked inside `PromoteIfConfiguredAsync` itself, so claiming
the configured address is not enough; the mailbox has to be reachable. That check matters since
OIDC sign-in: an account created from a Microsoft sign-in carries an email nobody proved, can log in
without confirming anything, and must therefore never inherit the role.

Self-delete and self-revoke are refused. Blocking self-revocation alone guarantees that no sequence
of calls can reach zero administrators.

### The known weakness: roles are cached in the token

Because roles travel in the JWT, a granted or revoked role only reaches a user in a token minted
afterwards:

> **A revoked administrator keeps access until their token expires**, up to `Jwt:LifetimeMinutes`
> (60 by default).

Fixing this needs server-side revocation — a denylist, or short-lived tokens with refresh — and is
out of scope. The tests assert the behaviour as it is rather than pretending otherwise.

## The OAuth state

`state` is the user id sealed with a **time-limited** Data Protection protector (10 minutes). It is
unforgeable without the server's keys and expires on its own.

This is the one place where getting it wrong would have been invisible and serious: a plain user id
in `state` would let anyone craft a callback and attach *their* GitHub account to *someone else's*
Dashboard user. See [ADR 011](decisions.md#adr-011--the-oauth-code-flow-by-hand-with-a-signed-time-limited-state).
The callback is anonymous on purpose — a browser redirect carries no `Authorization` header — and
the signed state is what replaces it.

## Signing in with Google or Microsoft

The OpenID Connect flow is written by hand, like the GitHub one
([ADR 016](decisions.md#adr-016--sign-in-with-google-and-microsoft-oidc-by-hand)). What protects it:

| Threat | Control |
|---|---|
| A forged or replayed callback | `state` is `{provider, nonce, PKCE verifier, binding}` sealed with a time-limited Data Protection protector (10 minutes), and the callback refuses one issued for another provider |
| **Login CSRF**: someone sends you a callback link carrying *their* code, so you end up working in their account | a random binding value travels both in the sealed state and in an `HttpOnly`, `SameSite=Lax` cookie set by `/authorize`; the callback requires both to match and then deletes the cookie |
| An intercepted authorization code | PKCE (S256): the code is useless without the verifier, which never leaves the server |
| A forged or replayed `id_token` | signature against the provider's published keys (refetched once on an unknown key, for rotation), issuer, audience = our client id, lifetime, and the `nonce` from the sealed state |
| **Account takeover by email** (Microsoft's "nOAuth": an Entra tenant admin can set any email on an account) | a Microsoft email never links to an existing account and never counts as verified. A Google email links only when Google asserts `email_verified` **and** our account confirmed the same address, so an unconfirmed account pre-registered by an attacker cannot be claimed either |
| The JWT leaking on the way back | the callback puts it in a URL **fragment**, which browsers never send to a server; the SPA stores it and replaces the history entry |
| An unverified address claiming `Admin:Email` | promotion requires `EmailConfirmed` (above) |

Known limits: two sign-ins started at once with the same provider in one browser overwrite each
other's binding cookie, so the first one fails with `invalid_state`. A Microsoft account can occupy an
email address it does not own, which blocks that address from registering with a password; it cannot
reach the real owner's account.

## Other hardening worth knowing

| Concern | Status |
|---|---|
| **SQL injection** | Not reachable: every query goes through EF Core with parameters. No string-concatenated SQL anywhere. |
| **Stored config injection** | A widget's configuration is validated against its declared `Parameters` before storage *and* before use: unknown keys are rejected, types and ranges enforced, string options checked against the allowed list. |
| **SSRF through widget config** | Provider URLs are built in code; user input is only ever an escaped query-string value. No widget takes a URL as a parameter. |
| **Error messages** | Only `WidgetDataException` messages reach a user. Every other exception is logged and replaced by a generic message, so provider internals and stack traces do not leak. |
| **Rate limiting** | **Absent.** Nothing limits login attempts or registrations per IP. Identity's lockout exists but is not configured. This is the most obvious missing control. |
| **The container user** | The API runs as the non-root `app` user from the base image. |
| **Dependencies** | Back-end versions are pinned exactly; the front end commits `package-lock.json` and the image installs with `npm ci`. No automated vulnerability scanning. |
| **Secret scanning** | Nothing prevents a secret being committed except `.gitignore` and review. |
