# Front end

For whoever works in `DashboardFrontEnd/`: how the React app is put together, how it talks to the
API, and the rules that keep it consistent. The endpoints it calls are documented in
[api.md](api.md).

## Running it

```bash
npm install
npm run dev      # http://localhost:5173
npm run build    # static files in dist/ — what the Dockerfile ships
npm run lint
```

> `npm run dev` proxies `/api` to **`localhost:8080`**, the Dockerised API — *not* to `dotnet run`
> on `:5119`. So `docker compose up -d` (at least `db`, `mailpit`, `server`) must be running, or
> every call 502s. To work against a local `dotnet run` instead, change the target in
> `vite.config.js`.

## The stack, in one table

| Concern | Choice |
|---|---|
| Framework | React 19 + Vite 8 |
| Language | plain JavaScript (`.jsx`) — no TypeScript, deliberately ([ADR 002](decisions.md#adr-002--react--vite-in-plain-javascript-no-typescript)) |
| Styling | plain CSS, one stylesheet per component, imported by the component. No Tailwind, no CSS-in-JS, no UI kit |
| Routing | `react-router-dom` |
| State | `useState` / `useEffect`; the two shared pieces, the token and the theme, live in `api.js` (`useSyncExternalStore`) and `localStorage` |
| Icons | inlined as React components, not `<img src="…svg">` |

The entire runtime dependency list is `react`, `react-dom`, `react-router-dom`.

Icons are inlined because they are recoloured with `currentColor` and therefore follow the current
theme or section colour automatically — a `+2.4 %` pill and its arrow take their green from one CSS
rule. An `<img>` pointing at an external SVG cannot do that.

## Structure

```
src/
  main.jsx              the router (see below)
  layouts.jsx           AppLayout (guard + taskbar + search), AuthLayout, RequireAdmin
  api.js                the only place that knows the token and builds requests
  useCurrentUser.js     GET /api/auth/me, cached per token
  useTheme.js           the light/dark hook
  widgetConfig.js       widget parameter forms: defaults, validation, refresh presets
  widgetLayout.js       the grid maths: default/min sizes, packing, stored positions
  index.css             design tokens (light/dark) + shared .glass, .button, .field
  App.jsx / App.css     the dashboard page
  services.jsx/.css     the services page and its connect / add-widget sheets
  admin.jsx/.css        the user management page
  login.jsx · register.jsx · confirm.jsx
  components/
    Taskbar.jsx/.css    brand, segmented navigation, search, theme switch, account menu,
                        and the floating tab bar that replaces the navigation on phones
    DashboardGrid.jsx   the 12-column grid, drag to reorder, drag to resize
    WidgetDialog.jsx    add a widget (pick → configure) or edit one
    WidgetForm.jsx      form fields generated from a widget's declared params
    Sheet.jsx           modal glass sheet built on <dialog>, docked at the bottom on phones
    ConfirmDialog.jsx   destructive confirmation (remove widget, disconnect, delete user)
    Notice.jsx          inline success / error banner
    ErrorBoundary.jsx   one bad payload cannot white-screen the app
    icons/Icons.jsx     every icon, inlined
    widgets/
      WidgetCard.jsx    the glass card shell: title, tools, body, handles
      DashboardWidget   one instance on the dashboard: card + menu + live body
      ChangePill.jsx    the +/- percentage pill
      bodies/           one renderer per widget key, and WidgetBody (pending / ok / error)
```

### Routes

| Path | Page | Access |
|---|---|---|
| `/login`, `/register`, `/confirm` | the auth pages | public; `/login` redirects to `/` when already logged in |
| `/` | the dashboard | logged in |
| `/services` | the catalogue and connections | logged in |
| `/admin` | user management | logged in **and** `roles` includes `Admin`; hidden from the navigation otherwise |

Every logged-in route sits under `AppLayout`, which redirects to `/login` **before rendering** when
there is no token, so a logged-out user never sees a half-loaded page — including after pressing Back
once logged out. After logging in, the user returns to the page that sent them to `/login`.

## The API client

`src/api.js` is the only module that reads or writes the `auth-token` key in `localStorage`, and the
only one that builds an `Authorization` header.

- `api.get / post / put / patch / delete(path, body)` — callers pass the full `/api/...` path. Bodies
  are sent as JSON, responses parsed, `204`s return `null`.
- Failures throw an `ApiError` carrying `status` and the parsed `problem` document, with a readable
  `message` (validation errors joined, or `detail`). `fieldErrors(error)` returns the `errors` map.
  A 5xx never shows its body to the user: in development it can be a full stack trace.
- `setToken`, `clearToken`, and `useToken()` — a `useSyncExternalStore` hook, so every component
  re-renders when the token changes, including when another tab logs out.
- **A `401` clears the token from that one place.** `AppLayout` sees the token disappear and
  navigates to `/login`. An expired token in the middle of polling therefore lands the user on the
  login page, not on a page of errors, and no caller has to handle it: they ignore `status === 401`.

## The dashboard

### Loading and polling

On mount the page reads `GET /api/widget-instances`, `GET /api/widgets` (names and parameters) and
`GET /api/services`. Results come from **one** interval polling `GET /api/widget-instances/data`
every 15 s. That endpoint reads the server's cache and never calls a provider, so polling it is
cheap; the per-widget `refreshRateSeconds` is the server's business and the browser does not mirror
it. The interval pauses while the tab is hidden and resumes, with an immediate read, when it comes
back. After adding or reconfiguring a widget the page reads the data once more 2.5 s later, because
the server fetches a new or changed instance straight away.

Each card renders a `WidgetBody` from its `{status, data, error, fetchedAt}` record: a skeleton for
`pending`, the payload for `ok`, and for `error` the message above **the last good data, dimmed**,
with its time. A widget key without a renderer shows a readable fallback, and each card has its own
error boundary.

### The grid

`position` is honoured as a 12-column CSS grid: `width` and `height` are spans of columns and of
rows (`--grid-row`, 88 px). `widgetLayout.js` holds the rules:

- **Order** is the stored layout read top to bottom, left to right (`y`, then `x`, then `id`).
- **Packing** places cards in that order, each in the first slot after the previous one that fits
  (the same sparse auto-placement as CSS grid), and writes the explicit `grid-column` / `grid-row`.
  The stored `x`/`y` are therefore always what is on screen.
- **Each widget key has a default and a minimum size.** A stored size below the minimum — the API's
  own default is `1×1` — is shown at the minimum.
- Under **900 px** the grid has 6 columns and cards are half or full width; under **600 px** it has a
  single column. Both reuse the same order, so moving a card on a phone moves it on a desktop too.

Moving and resizing are hand-written pointer events, no library:

- The dotted handle drags a card. Entering another card moves the dragged one to its place in the
  order; the others glide to their new slot with a FLIP animation (Web Animations API, skipped under
  `prefers-reduced-motion`).
- The bottom-right handle resizes in whole columns and rows, clamped to the widget's minimum and to
  12. It is only offered at the 12-column width.
- Both handles also work from the keyboard: arrow keys move the card through the order, or change
  its size.
- **Nothing is sent while the pointer moves.** On release the layout is packed into 12-column
  positions, and one `PATCH /api/widget-instances/{id}/position` is sent for each instance whose
  position actually changed. The local state is updated first; if a request fails the instances are
  reloaded and a banner says so.
- Dragging is disabled while the search field filters the page, since the order of a filtered list
  is ambiguous.

### Adding and editing

`WidgetDialog` lists every widget grouped by service. Widgets with `available: false` are greyed out,
with a link to `/services`. The form is generated from the widget's `params`: `options` become a
`<select>`, integers get `min`/`max`, optional empty fields are **omitted**, not sent as `""`. The same
rules are checked client-side before anything is sent. The refresh rate is a set of presets between
30 s and 24 h. A `403` says to connect the service first; a `400` puts each field error under its
field.

Editing uses the same form, prefilled from `instance.config`, and sends `PUT` (config and refresh
rate only — never the position).

## The services page

Each card shows a status pill and its primary action from `service.auth` and `service.subscribed`:

| `auth` | Pill | Actions |
|---|---|---|
| `none` | Ready to use | Add a widget |
| `apiKey`, not subscribed | Needs an API key | Connect → a form from `credentialPrompt`: `suggestedValue` prefilled, `help`, and `signupUrl` as a link → `POST /api/subscriptions/{key}`. A `422` shows the provider's refusal. |
| `apiKey`, subscribed | Connected | Add a widget · Disconnect |
| `oauth`, not subscribed | Link your account | `GET /api/services/{key}/oauth/authorize`, then the browser goes to the returned URL. A `503` turns the pill into "Not available on this deployment". |
| `oauth`, subscribed | Connected | Disconnect (and Add a widget if the service has any) |

Disconnect asks for confirmation and calls `DELETE /api/subscriptions/{key}`.

The OAuth callback lands back on `/services?github=linked&account=…` or `?github=failed&reason=…`;
the page turns that into a banner and removes the query string so a reload does not repeat it.

**Add a widget** opens the service's widgets, each with **Preview** (`GET /api/widgets/{key}/preview`,
a one-off check that is never polled, since it hits the provider) and **Add widget**.

## The admin page

`/admin` lists `GET /api/admin/users` with grant / revoke and delete. Your own row's revoke and delete
are disabled rather than left to fail, because the API refuses both with `400`. After a role change
the page notes that it only takes effect on that user's next login, since roles travel in the JWT.

## All API calls are relative

Every call is a relative `/api/...` path. nginx rewrites `/api/` → `server:8080/` in Docker, and
`vite.config.js` declares the same rewrite for the dev server.

> **Do not introduce a `VITE_API_URL`.** Vite inlines environment variables at build time, so an API
> address in one would be baked into the image and need one image per environment. Keeping URLs
> relative is what lets a single built image run anywhere
> ([ADR 012](decisions.md#adr-012--relative-api-paths-behind-nginx-rather-than-a-configured-api-url)).

## Theming

The visual language is iOS: system colours, a "liquid glass" material (translucent surfaces with
`backdrop-filter`, a hairline border and a soft top highlight), large titles, capsule buttons,
segmented controls, sheets and a floating tab bar. Every colour lives in `src/index.css` as CSS
variables, in two token sets:

```css
:root, :root[data-theme='light'] { --color-bg-page: #f2f2f7; --glass-bg: rgba(255,255,255,.74); … }
:root[data-theme='dark']         { --color-bg-page: #000000; --glass-bg: rgba(28,28,30,.74); … }
```

Components only ever read `var(--color-*)` / `var(--glass-*)` — **never a raw hex value**. That rule
is what makes the whole app re-theme from a single attribute change. Without `backdrop-filter`
support, or with `prefers-reduced-transparency`, the glass tokens fall back to opaque surfaces.

`useTheme.js` owns the choice: `useState` initialised from `localStorage` (`dashboard-theme`), falling
back to the OS's `prefers-color-scheme`, and a `useEffect` that writes
`document.documentElement.setAttribute('data-theme', theme)` and stores the value. Both layouts call the hook, so the
auth pages follow the saved theme too.

`ThemeToggle` (inside `Taskbar.jsx`) is a real `<button role="switch">` with `aria-checked`, not a
decorative div.

## Responsive strategy

The Figma file only has a 1440 px desktop frame, so the smaller layouts are our own adaptation of it.

- **The widget grid** has 12 columns above 900 px, 6 between 600 and 900 px (cards half or full
  width), and 1 below 600 px — see [The grid](#the-grid). The services grid goes 3 → 2 → 1 columns at
  the same breakpoints.
- **Horizontal padding is `4%`** of the viewport on the taskbar and the page, so the gutter stays
  proportional; below 600 px it becomes 16 px plus the safe-area inset.
- **The taskbar degrades** instead of overflowing: the "Dashboard" wordmark hides under 900 px. Under
  **640 px** the segmented navigation becomes a floating glass **tab bar** at the bottom of the
  screen, and the search field collapses to a button that expands over the bar with a Cancel button.
- **Sheets** are centred on larger screens and dock to the bottom, with a grabber, on phones.
- **Font sizes stay in `px`** (with one `clamp()` on the large title). Scaling type as a percentage of
  an already-fluid parent compounds unpredictably.

Verified in the browser at 1440 px, 760 px and 390 px, in both themes.

## Conventions

- One component, one stylesheet, imported by the component. Class names are prefixed by the
  component (`toolbar__title`, `service-dialog__field`) — BEM-ish, and enough to avoid collisions
  without a CSS module system.
- Comments are sparse by choice: write one only where it explains a *why* the code cannot.
- A new widget renderer means one line in `bodies/WidgetBody.jsx`'s `widgetKey → component` map,
  and a default/minimum size in `widgetLayout.js`. Nothing else changes.
- Dialogs are `Sheet`s (a native `<dialog>` opened with `showModal`), which gives focus trapping,
  Escape to close and an inert page behind for free. Focus returns to the opener on close.
- Icon-only buttons carry an `aria-label`; state is never conveyed by colour alone (pills have words).
