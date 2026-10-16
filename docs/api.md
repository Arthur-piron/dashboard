# API reference

Every route the server exposes.

## Conventions

**Base URL.** The API's own routes have no prefix. The browser reaches them through nginx, which
strips an `/api` prefix:

| From | Call |
|---|---|
| the front end (or the browser, via nginx on `:8081`) | `/api/services` |
| the API directly, in Docker | `http://localhost:8080/services` |
| the API directly, with `dotnet run` | `http://localhost:5119/services` |

**Authentication.** Everything except `/about.json`, the four `/auth` routes and the OAuth callback
requires a header:

```
Authorization: Bearer <the token from POST /auth/login>
```

A missing or expired token is `401`. The token lasts 60 minutes by default.

**JSON.** Property names are camelCase; enums are camelCase strings (`"none"`, `"apiKey"`, `"oauth"`,
`"pending"`, `"ok"`, `"error"`). Timestamps are UTC ISO 8601, except `/about.json`'s
`current_time`, which is a Unix timestamp because the delivery specifies it.

**Errors.** Validation failures are
[problem documents](https://datatracker.ietf.org/doc/html/rfc9457) with an `errors` map:

```json
{ "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "refreshRateSeconds": ["Must be between 10 and 86400 seconds."] } }
```

**Ownership.** A widget instance or subscription belonging to another user returns **404, not 403** —
the existence of an id is never revealed. `/admin` is the documented exception and returns **403** to
a non-administrator.

---

## Status codes used, and what they mean here

| Code | Meaning in this API |
|---|---|
| `200` | here is the result |
| `201` | created; `Location` points at it |
| `202` | accepted — registration, where the email is sent asynchronously |
| `204` | done, nothing to return |
| `302` | only the OAuth callback, redirecting the browser back to the SPA |
| `400` | malformed or invalid input, or a request that makes no sense for this service |
| `401` | no token, or an invalid or expired one |
| `403` | authenticated but not allowed: email not confirmed, not subscribed, not an administrator |
| `404` | no such route, or no such row **for you** |
| `422` | the request was valid but the provider refused or could not answer |
| `503` | a feature is not configured on this deployment (GitHub OAuth) |

---

## Service check

### `GET /`

Anonymous. Returns the plain text `DashboardBackEnd is running.`

### `GET /about.json`

Anonymous. The catalogue as the delivery specifies it: the caller's IP, the server time, and every
service with its widgets and their parameter types.

```json
{
  "client": { "host": "172.18.0.4" },
  "server": {
    "current_time": 1759600000,
    "services": [
      { "name": "crypto",
        "widgets": [
          { "name": "crypto.history",
            "description": "Price of a coin over the last days, with its low, high and change over the period.",
            "params": [ { "name": "coin", "type": "string" },
                        { "name": "days", "type": "integer" },
                        { "name": "currency", "type": "string" } ] }
        ] }
    ]
  }
}
```

Services and widgets are sorted by key. `type` is only ever `integer` or `string`. This is also the
quickest way to tell which services are compiled into a running image.

---

## Auth

### `POST /auth/register`

Anonymous.

```json
{ "username": "arthur", "email": "arthur@example.com", "password": "Password123" }
```

| Response | When |
|---|---|
| `202` | created; a confirmation email was sent (or attempted — a send failure is logged, not surfaced) |
| `400` | Identity rejected it, with its own codes as keys: `PasswordTooShort`, `PasswordRequiresUpper`, `DuplicateUserName`, `DuplicateEmail` |

Password policy: at least 8 characters, with an upper case letter, a lower case letter and a digit.
Non-alphanumerics are not required. Emails must be unique.

If the address matches `Admin:Email`, the account is promoted to administrator once it confirms the
address (below), not here: promotion needs a proven mailbox.

### `GET /auth/confirm?userId={id}&token={token}`

Anonymous. Both values come from the link in the email.

| Response | Body |
|---|---|
| `200` | `"Email confirmed. You can now log in."` or `"Email already confirmed."` |
| `400` | `"Invalid confirmation link."` or `"Invalid or expired confirmation link."` |

The email links to the **front end** (`{Frontend:Url}/confirm?...`), which calls this route. A successful
confirmation is also where the configured administrator is promoted.

### `POST /auth/login`

Anonymous.

```json
{ "email": "arthur@example.com", "password": "Password123" }
```

| Response | |
|---|---|
| `200` | `{ "token": "eyJ…", "expiresAt": "2026-10-05T15:04:05Z" }` |
| `401` | wrong password **or** unknown email — deliberately indistinguishable |
| `403` | `type: "email_not_confirmed"` — confirm the address first. Expected, not a bug. |

### `GET /auth/me`

Authenticated. Reads the token only; no database access.

```json
{ "id": "a1b2…", "username": "arthur", "email": "arthur@example.com", "roles": ["Admin"] }
```

## Sign in with Google or Microsoft (OIDC)

OpenID Connect, authorization code flow with PKCE. All three routes are **anonymous**: the user is
signing in, so there is no JWT yet. The first sign-in creates the account, and later ones find it
again by the provider's `sub`.

### `GET /auth/oidc/providers`

The providers this deployment has configured; the login page builds its buttons from this.

```json
[ { "key": "google", "name": "Google" }, { "key": "microsoft", "name": "Microsoft" } ]
```

### `GET /auth/oidc/{provider}/authorize`

`{provider}` is `google` or `microsoft`. Returns `{ "url": "https://accounts.google.com/…" }` for the
SPA to navigate to, and sets an `HttpOnly` cookie `oidc-binding-{provider}` that the callback checks.
Call it with `fetch` from the page that will navigate, so the cookie lands in the same browser.

| Response | When |
|---|---|
| `200` | `{ url }` |
| `404` | unknown provider |
| `503` | `Oidc:{Google\|Microsoft}:ClientId`/`ClientSecret` unset, or the provider's discovery document unreachable |

### `GET /auth/oidc/{provider}/callback?code=…&state=…`

Where the provider sends the browser back. It always answers with a **redirect** to the front end:

| Outcome | Redirect |
|---|---|
| signed in | `{Frontend:Url}/auth/callback#token=<jwt>&expiresAt=<iso>`: a fragment, so the JWT never reaches a server or an access log |
| failed | `{Frontend:Url}/login?oidc=failed&provider=<key>&reason=<reason>` |

| `reason` | Meaning |
|---|---|
| `denied` | the user refused on the provider's consent screen (`error=access_denied`) |
| `provider_error` | the provider refused our request itself, with any other `error`, usually a misconfigured app registration; its `error_description` is logged server-side |
| `invalid_request` | no `code` or no `state` |
| `invalid_state` | the state is tampered, older than 10 minutes, issued for another provider, or the binding cookie is missing or different (another browser, or a replay) |
| `exchange_failed` | the provider refused the code, e.g. a wrong PKCE verifier |
| `invalid_token` | the `id_token` failed validation: signature, issuer, audience, lifetime or nonce |
| `unavailable` | the provider could not be reached |
| `no_email` | the token carries no email address |
| `email_in_use` | an account already has this email and the provider does not prove ownership of it (Microsoft always, Google with `email_verified: false`) |
| `email_not_confirmed` | Google verified the email, but the matching Dashboard account never confirmed it |
| `account_failed` | the account could not be created or linked; logged server-side |

**Matching an existing account:** a verified Google email links to the Dashboard account with that
address **if** that account confirmed it; a Microsoft email never links. Accounts created by a sign-in
have no password; `EmailConfirmed` is `true` for a Google-verified email and `false` for Microsoft.

---

## Catalogue

### `GET /services`

Authenticated. Every service, with whether *you* can use it.

```json
[ { "key": "nasa", "name": "NASA",
    "description": "Astronomy Picture of the Day and Mars rover photos, with your own NASA API key.",
    "auth": "apiKey", "subscribed": false,
    "credentialPrompt": { "label": "API key",
                          "help": "Paste your own key from api.nasa.gov: it is free and arrives by email at once.",
                          "suggestedValue": null, "signupUrl": "https://api.nasa.gov" } } ]
```

`credentialPrompt` is non-null only for `apiKey` services. `subscribed` is true for every `none`
service, since they need no subscription.

### `GET /widgets`

Authenticated. Every widget with its declared parameters.

```json
[ { "key": "weather.current", "serviceKey": "weather", "name": "Current weather",
    "description": "Temperature, humidity, wind and sky overview for a city right now.",
    "params": [ { "name": "city", "type": "string", "required": true,
                  "description": "City name, e.g. Paris", "options": null, "min": null, "max": null },
                { "name": "unit", "type": "string", "required": false,
                  "description": "Temperature unit", "options": ["celsius", "fahrenheit"],
                  "min": null, "max": null } ],
    "available": true } ]
```

`available` is false when you are not subscribed to the widget's service. This is the endpoint a
"configure widget" form should build itself from.

### `GET /widgets/{key}/preview?<params>`

Authenticated. Fetches **live** from the provider, without storing anything — the only route that
calls a widget's `FetchAsync` directly. Parameters go in the query string; integers are parsed
according to the widget's declaration.

```
GET /widgets/weather.current/preview?city=Lille&unit=celsius
```

| Response | When |
|---|---|
| `200` | the widget's payload, whatever shape that widget returns |
| `400` | the config is invalid: missing required parameter, wrong type, out of range, not in `options`, or an unknown parameter |
| `403` | you are not subscribed to this widget's service |
| `404` | no widget with that key |
| `422` | the provider refused or could not answer — unknown city, unknown coin, rate limit. The `detail` is safe to show the user. |

---

## Subscriptions

### `GET /subscriptions`

Authenticated. **Never returns credentials.**

```json
[ { "serviceKey": "nasa", "createdAt": "2026-10-01T09:12:00Z", "expiresAt": null } ]
```

### `POST /subscriptions/{serviceKey}`

Authenticated. For `apiKey` services only.

```json
{ "apiKey": "<your NASA API key>" }
```

| Response | When |
|---|---|
| `204` | stored, encrypted. Calling it again **replaces** the credential rather than failing. |
| `400` | the service needs no subscription (`none`) or is linked through OAuth instead |
| `400` | `apiKey` was missing or blank |
| `404` | no such service |
| `422` | the provider rejected the key — validated *before* storing |
| `503` | the provider could not check the key (rate limited, or down), which says nothing about the key — nothing is stored |

### `DELETE /subscriptions/{serviceKey}`

Authenticated. Always `204`, whether or not a row existed. This is also how a linked GitHub account
is unlinked.

Widgets of that service keep their instances but start reporting an error on refresh, telling the
user to subscribe again.

---

## GitHub OAuth

Both routes answer **503** while `GitHub:ClientId` / `GitHub:ClientSecret` are unset.

### `GET /services/github/oauth/authorize`

Authenticated. Returns the URL the browser must be sent to:

```json
{ "url": "https://github.com/login/oauth/authorize?client_id=…&redirect_uri=…&state=…" }
```

`state` is the user id sealed with a time-limited protector (10 minutes).

### `GET /services/github/oauth/callback?code=…&state=…`

**Anonymous on purpose** — GitHub redirects the browser here, and a browser navigation carries no
`Authorization` header. The signed `state` is what identifies the user.

Always a `302` back to the SPA:

| Redirect | When |
|---|---|
| `{Frontend:Url}/services?github=linked&account=<login>` | success |
| `{Frontend:Url}/services?github=failed&reason=denied` | the user refused on GitHub |
| `…&reason=invalid_request` | `code` or `state` missing |
| `…&reason=invalid_state` | the state was forged or expired |
| `…&reason=unknown_user` | the account in the state no longer exists |
| `…&reason=exchange_failed` | GitHub refused the code exchange |

Opening the `authorize` URL by hand in the address bar returns **401**, and that is correct: a
navigation carries no token.

---

## Widget instances

A widget instance is a widget you placed: a key, your configuration, a refresh rate and a grid
position.

```json
{ "id": 7, "widgetKey": "weather.current", "serviceKey": "weather",
  "config": { "city": "Lille", "unit": "celsius" },
  "refreshRateSeconds": 600,
  "position": { "x": 0, "y": 0, "width": 2, "height": 1 },
  "createdAt": "2026-10-01T09:12:00Z", "updatedAt": "2026-10-01T09:12:00Z" }
```

All routes are authenticated and scoped to you; another user's instance is `404`.

### `GET /widget-instances`

Your instances, ordered by id.

### `POST /widget-instances`

```json
{ "widgetKey": "weather.current",
  "config": { "city": "Lille", "unit": "celsius" },
  "refreshRateSeconds": 600,
  "position": { "x": 0, "y": 0, "width": 2, "height": 1 } }
```

`position` is optional and defaults to `{0,0,1,1}`.

| Response | When |
|---|---|
| `201` | created, with `Location: /widget-instances/{id}`. It is scheduled for refresh **immediately**. |
| `400` | unknown `widgetKey`, invalid `config`, `refreshRateSeconds` outside 10…86400, or an invalid `position` |
| `403` | not subscribed to the widget's service |

Limits: `refreshRateSeconds` between **10** and **86400** (a day); `x`,`y` ≥ 0; `width`,`height`
between **1** and **12**.

### `GET /widget-instances/{id}`

One instance, or `404`.

### `PUT /widget-instances/{id}`

Reconfigure. Takes the config and the refresh rate, **not** the position.

```json
{ "config": { "city": "Paris", "unit": "celsius" }, "refreshRateSeconds": 300 }
```

`200` with the updated instance, which is also rescheduled to refresh immediately. `400` on invalid
input, `404` if it is not yours.

### `PATCH /widget-instances/{id}/position`

```json
{ "x": 2, "y": 0, "width": 1, "height": 1 }
```

`200` with the updated instance. Separate from `PUT` so that dragging a widget never re-validates or
re-fetches its data.

### `DELETE /widget-instances/{id}`

`204`, or `404` if it is not yours. Also removes it from the refresh schedule.

### `GET /widget-instances/{id}/data`

**Reads the cache the background timer fills. It never calls the provider.**

```json
{ "instanceId": 7, "widgetKey": "weather.current", "status": "ok",
  "fetchedAt": "2026-10-05T12:00:00Z", "nextRefreshAt": "2026-10-05T12:10:00Z",
  "data": { "city": "Lille", "temperature": 12.4, "…": "…" },
  "error": null }
```

| `status` | Meaning |
|---|---|
| `pending` | scheduled, not fetched yet — normal right after creation or a server restart |
| `ok` | `data` is current as of `fetchedAt` |
| `error` | `error` says what went wrong, and `data` still holds the **last good payload** if there was one |

`404` if the instance is not yours.

### `POST /widget-instances/{id}/refresh`

**Calls the provider now** instead of waiting for `nextRefreshAt`, and answers with the new record,
same shape as `GET /widget-instances/{id}/data`. The next scheduled refresh restarts from now.

The current record is returned **without** a provider call when a refresh is already running, or
when the record was fetched less than `Timer:ManualRefreshCooldownSeconds` ago (default 10, the same
floor as the minimum refresh rate). So clicking refresh repeatedly cannot drain a provider quota. A
`pending` record is never on cooldown.

`404` if the instance is not yours.

### `GET /widget-instances/data`

The same records for all of your instances, in one call — what a dashboard should poll.

---

## Admin

All routes require the `Admin` role and return **403** to any other authenticated user (not 404 —
see [security.md](security.md#why-admin-answers-403-where-everything-else-answers-404)).

### `GET /admin/users`

```json
[ { "id": "a1b2…", "username": "arthur", "email": "arthur@example.com",
    "emailConfirmed": true, "createdAt": "2026-09-30T08:00:00Z",
    "roles": ["Admin"], "widgetInstances": 4, "subscriptions": 2 } ]
```

Ordered by creation date. A DTO by design: no password hash, no security stamp, no credential
material — a test asserts it.

### `DELETE /admin/users/{id}`

Deletes the account; the cascade takes its widget instances and subscriptions, and each instance is
removed from the refresh schedule.

`204` on success, `404` if no such user, **`400` if `id` is your own** — an administrator cannot
delete themselves.

### `POST /admin/users/{id}/admin`

Grants the role. `204`, and `204` again if they already had it (idempotent). `404` if no such user.

### `DELETE /admin/users/{id}/admin`

Revokes it. `204`, idempotent, `404` if no such user, **`400` if `id` is your own** — refusing
self-revocation is what guarantees at least one administrator always remains.

> A revoked administrator keeps access until their current token expires (up to 60 minutes), because
> roles travel in the JWT. See
> [ADR 013](decisions.md#adr-013--roles-in-the-jwt-and-an-administrator-bootstrapped-from-configuration).
