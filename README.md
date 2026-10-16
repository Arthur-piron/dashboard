# Dashboard

![C#](https://img.shields.io/badge/C%23-512BD4?logo=dotnet&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET_10-512BD4?logo=dotnet&logoColor=white)
![React](https://img.shields.io/badge/React_19-20232A?logo=react&logoColor=61DAFB)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=white)
![Tests](https://img.shields.io/badge/tests-193_xUnit-success)

A personal dashboard in your browser.

You create an account, pick the **services** you care about — the weather, a cryptocurrency,
NASA, Duolingo, your GitHub account — and place **widgets** from those services on a grid.
Each widget is configured by you (which city, which coin, which rover) and refreshes on its own
schedule, so the page always shows current information without you reloading anything.

Think of it as the home screen of a phone, for web services.

```
┌──────────────────────────────────────────────────────────┐
│  Dashboard                              ☀/☾      ( AP )  │
├──────────────────────────────────────────────────────────┤
│                                                          │
│   ┌────────────────┐  ┌────────────┐  ┌───────────────┐  │
│   │ Weather        │  │ Bitcoin    │  │ Picture of    │  │
│   │ Lille  12°C    │  │ 61 240 €   │  │ the day       │  │
│   │ wind 14 km/h   │  │ +2.4 %     │  │ [ image ]     │  │
│   │ every 10 min   │  │ every 60 s │  │ every 6 h     │  │
│   └────────────────┘  └────────────┘  └───────────────┘  │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

This is an Epitech group project (2 students), built as a .NET back end and a React front end
that run together in Docker.

### My part

I built most of the **React front end**: registration, email confirmation and login pages, the
services page (API key and GitHub OAuth subscription flows), the widget grid (add, configure,
move, resize, remove, per-widget refresh rate), the visual style, and the installation guide.

### Stack

| Layer | Technology |
|---|---|
| API | .NET 10 minimal API, ASP.NET Core Identity, JWT, EF Core |
| Database | PostgreSQL 17 |
| Front end | React 19, React Router, Vite, served by nginx |
| Auth | Email + password, Google and Microsoft (OpenID Connect), GitHub OAuth |
| Tests | xUnit integration tests against a real database and mailbox (Mailpit) |
| Run | Docker Compose |

## What it can do today

- **Create an account**, confirm it by email, and log in — or skip the password entirely with
  **Continue with Google** or **Continue with Microsoft** (an Epitech account).
- **Subscribe to a service.** Some need nothing (Weather, Crypto, Duolingo). NASA asks for a
  free API key. GitHub is linked in one click through its own consent page, so no password or
  key is ever typed into this application.
- **Add widgets** with your own settings and your own refresh rate (from 10 seconds to a day).
- **Keep the data fresh automatically.** The server refreshes every widget in the background,
  and the page simply displays the latest result — so a hundred open tabs do not mean a hundred
  calls to the weather provider.
- **Administer the platform.** A configured administrator can list accounts, delete one, and
  grant or revoke the administrator role.

### The catalogue

5 services, 11 widgets. The delivery requires at least 3 services and 6 widgets for a group of two.

| Service | Needs | Widgets |
|---|---|---|
| **Weather** (Open-Meteo) | nothing | Current weather · Forecast |
| **Crypto** (CoinGecko) | nothing | Coin price · Price history |
| **Duolingo** | nothing | Learning profile · Course progress |
| **NASA** | a free API key | Astronomy Picture of the Day · Mars rover photos |
| **GitHub** | linking your account (OAuth) | GitHub profile · Repositories · Recent activity |

## Try it

You need [Docker](https://docs.docker.com/get-docker/). Nothing else — no .NET, no Node.

### Installing Docker

If `docker compose version` already prints a version, skip this.

| System | What to install |
|---|---|
| **Windows / macOS** | [Docker Desktop](https://www.docker.com/products/docker-desktop/), which includes Compose. Start it once before running the commands below. |
| **Linux** | [Docker Engine](https://docs.docker.com/engine/install/) and the [Compose plugin](https://docs.docker.com/compose/install/linux/). Then `sudo usermod -aG docker $USER` and log out and back in, so `docker` runs without `sudo`. |

You also need [Git](https://git-scm.com/downloads) to clone the repository.

### Running it

```bash
git clone https://github.com/Arthur-piron/dashboard.git
cd dashboard
cp .env.example .env   # optional: every variable has a working local default
docker compose build
docker compose up
```

Then open:

| | |
|---|---|
| **The app** | <http://localhost:8081> |
| **The mailbox** (confirmation emails land here, not in a real inbox) | <http://localhost:8025> |
| The API's self-description | <http://localhost:8080/about.json> |

Register an account, open <http://localhost:8025>, click the link in the message you received,
and log in.

`docs/deployment.md` explains every piece of that stack, and `docs/user-guide.md` walks through
the application screen by screen.

## Documentation

Pick the document for what you want to do.

| Document | For |
|---|---|
| [docs/user-guide.md](docs/user-guide.md) | **Using the application**, with no technical background. |
| [docs/architecture.md](docs/architecture.md) | How the pieces fit together, and how a widget's data travels from a provider to your screen. |
| [docs/technology.md](docs/technology.md) | **The technologies we chose and why**, each against the alternatives we rejected. |
| [docs/decisions.md](docs/decisions.md) | **The design decisions**, one by one: the problem, the options, the choice, and what it costs us. |
| [docs/deployment.md](docs/deployment.md) | **Containerisation and deployment**: images, Compose, nginx, volumes, going to production. |
| [docs/security.md](docs/security.md) | **Secrets management**, authentication, and how third-party credentials are protected. |
| [docs/backend.md](docs/backend.md) | Working on the API: project layout, the widget catalogue, migrations, tests, and how to add a service. |
| [docs/frontend.md](docs/frontend.md) | Working on the React app: structure, the API client, the grid, theming, responsive rules. |
| [docs/api.md](docs/api.md) | Every endpoint: route, authentication, request, responses. |

### For reviewers

| Looking for | Where |
|---|---|
| Technology choices compared with their alternatives | [docs/technology.md](docs/technology.md#comparisons-layer-by-layer): one comparison table per layer, from the back-end framework to deployment |
| Why each design decision was made | [docs/decisions.md](docs/decisions.md): 16 decisions, each with its **Context** and **Decision** |
| The alternatives considered | the **Options** table of each decision |
| The impact of each choice | **What it buys** and **What it costs** in each decision |
| What we would propose instead next time | **Alternative we would propose for a second iteration** in each decision |
| Secrets management | [docs/security.md](docs/security.md#secrets-management) |
| Containerisation | [docs/deployment.md](docs/deployment.md) |
| The `/about.json` contract and every endpoint | [docs/api.md](docs/api.md) |

## Repository layout

```
DashboardBackEnd/        the API (.NET 10, minimal API)
DashboardBackEnd.Tests/  the integration test suite (xUnit, 193 tests)
DashboardFrontEnd/       the React app, and the nginx config that serves it
docs/                    the documentation above
bonus/                   extras that are not part of the required delivery
docker-compose.yml       the whole stack: database, mailbox, API, web client
```

## Project status

The back end is feature-complete for the delivery, with an integration test per behaviour.

The front end is wired end to end: registration, email confirmation, login and logout, the services
page with the real subscribe flow (API key and GitHub OAuth), and the dashboard itself — adding,
configuring, moving, resizing and removing widgets on a 12-column grid, with live data polled from
the server's cache. Administrators get a user management page.
