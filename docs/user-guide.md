# User guide

For someone who just wants to use Dashboard. No technical background needed — if a step mentions a
command, it is only for whoever installs the application for you.

## What Dashboard is for

Dashboard is a single page that gathers information you would otherwise check on five different
websites: the weather where you live, the price of a cryptocurrency, NASA's picture of the day, your
Duolingo streak, your GitHub account.

You choose which **services** to use, then place **widgets** from those services on your page. Each
widget is yours: you decide which city the weather widget shows, and how often it refreshes. The
application keeps everything up to date by itself — you never have to reload the page.

## Before you start

Someone needs to have started the application. If you are doing that yourself, you need Docker, and
then:

```bash
docker compose build
docker compose up
```

Once it is running, you have two addresses:

| Address | What it is |
|---|---|
| <http://localhost:8081> | **Dashboard** — the application |
| <http://localhost:8025> | **the mailbox** — where the application's emails arrive |

That second address needs explaining. During development the application does not send real email;
it delivers everything to a local mailbox you open in your browser. **The confirmation email will
not arrive in your real inbox** — look for it at <http://localhost:8025>.

## Creating your account

### 1. Register

Go to <http://localhost:8081/register> and fill in a username, your email address and a password.

Your password must be **at least 8 characters** and contain **an upper case letter, a lower case
letter and a digit**. `Password123` is valid. Symbols are allowed but not required.

If something is wrong, the page tells you: the password is too short, the address is already taken,
and so on. Nothing is created until it is accepted.

### 2. Confirm your address

After registering, the application sends you a confirmation email. **You cannot log in until you
have opened it** — this is on purpose, so that nobody can create an account with someone else's
address.

Open <http://localhost:8025>, click the most recent message, and click the link inside it. You land
on a page that confirms your address is verified.

If you try to log in before this step, you get a message saying your email is not confirmed. That is
expected — go and click the link.

### 3. Log in

Go to <http://localhost:8081/login> and enter the email address and password you registered with.

You stay logged in for **one hour**. After that you simply log in again. If a page suddenly looks
empty or stops loading, that is usually what has happened.

If you cannot log in:

| | |
|---|---|
| *"Invalid email or password"* | one of the two is wrong. For safety the application does not say which. |
| *"Email not confirmed"* | go back to step 2. |
| The email never arrived | check <http://localhost:8025> rather than your real inbox. There is currently **no way to ask for a second confirmation email**, so if the message is really not there, ask whoever runs the application. |

### Or: continue with Google or Microsoft

If whoever runs Dashboard has set it up, the login and register pages also show **Continue with
Google** and **Continue with Microsoft**. Click one, choose your account on the provider's page, and
you land on your dashboard: no password, and no confirmation email to wait for. The first time
creates your Dashboard account, and the next times find it again.

A few things are worth knowing:

- **Google, with an account you already have here:** if you registered with the same Gmail address
  *and* confirmed it, Google simply signs you into that account.
- **Microsoft, with an address already registered here:** Dashboard refuses and asks you to log in
  with your password. Microsoft does not prove that an address belongs to the person using it, so
  Dashboard will not hand an existing account over on its word.
- **Microsoft means your Epitech account.** The application is registered in Epitech's directory,
  so personal Outlook or Hotmail accounts are refused for now.
- **An account created this way has no password,** so keep signing in with the same button.

## Choosing your services

Open the **Services** page (<http://localhost:8081/services>). You see a card for each service,
saying what it offers and what it needs from you.

There are three kinds.

### Services that need nothing

**Weather**, **Crypto** and **Duolingo** work straight away. They use public information — a city's
weather, a coin's price, a public Duolingo profile — so there is nothing to connect and nothing to
set up.

### A service that needs a key

**NASA** needs an API key: a short code that identifies whoever is calling NASA's servers. Get your
own from the signup link on the card (<https://api.nasa.gov>). It is free and takes a minute: you give
an email address and the key arrives at once.

The application checks a key with NASA before saving it, so a typo is caught there and then rather
than becoming a widget that never works. Once saved, **the key is encrypted and never shown again** —
not on any page, not to anyone, including administrators.

### A service you link to your account

**GitHub** works differently, and this is the safest kind. You never type a GitHub password or token
into Dashboard. Instead, clicking Connect sends you to GitHub's own page, where you approve access
while logged in to GitHub. GitHub then sends you back, and Dashboard holds only a token that lets it
read what you approved.

You can revoke that at any time, from either side: disconnect the service in Dashboard, or remove the
application from your GitHub account settings.

### Trying a widget before committing

On a connected service, **Add a widget** lists that service's widgets. Each one opens a small form
built from the widget's own options — a city for the weather, a coin name for crypto, which Mars rover
for NASA. **Preview** fetches that data once and shows you the result; **Add widget** places it on
your dashboard.

Previewing is a good way to check that you typed a city name the way the provider expects, before you
place a widget on your page.

## Your widgets

Each widget you add has three things you control:

- **Which widget** — from the services you can use.
- **Its options** — the city, the coin, the number of days, the rover. Each widget declares its own,
  and the form only accepts valid values: a number stays within its range, and a choice list only
  offers real choices.
- **Its refresh rate** — how often it fetches new information, from **10 seconds** to **once a day**.

Pick refresh rates that match the information. A weather widget every 10 minutes is sensible; every
10 seconds adds nothing, because the weather report itself does not change that fast. For NASA, a slow
rate is also what keeps you under your key's rate limit.

Refreshing happens **on the server**, not in your browser. Closing the page does not stop it, and
opening Dashboard in three tabs does not triple the work.

A widget tells you how it is doing:

| It shows | Meaning |
|---|---|
| its data | all good — it also knows when it last checked |
| *waiting for the first refresh* | it has just been added, or the application has just restarted. A moment. |
| a message, with the old data still visible | something went wrong — an unknown city, a provider that is down, a rate limit. The application keeps showing the last good information and keeps retrying by itself. You do not have to do anything. |

Most error messages come from the provider and are worth reading: an unknown city is usually a
spelling the provider does not recognise, and a coin has to be named the way CoinGecko names it
(`bitcoin`, not `BTC`).

### Arranging your dashboard

The dashboard page (<http://localhost:8081/>) shows your widgets on a grid.

- **Add widget**, at the top right, lets you pick any widget from the services you can use. Widgets
  of a service you have not connected yet are greyed out, with a link to the Services page.
- Each widget's **⋯** menu offers **Edit widget** (change its options or refresh rate) and **Remove
  widget**, which asks for confirmation first.
- **Drag** a widget by the dotted handle in its top-right corner to move it. On a computer, drag the
  bottom-right corner to make it wider or taller. With the keyboard, focus a handle and use the arrow
  keys.
- The **search** field in the top bar filters the widgets on the page. It filters the services on the
  Services page in the same way.

Your layout is saved as you change it, so reloading the page, or logging in from another computer,
shows the dashboard exactly as you left it. On a phone, widgets are stacked in a single column and the
navigation moves to a bar at the bottom of the screen.

## Disconnecting a service

Disconnecting a service deletes the key or token the application held for it. Nothing is kept.

Widgets from that service stay on your page but stop updating, and say so — so reconnecting brings
them back to life rather than making you set them up again.

## Switching between light and dark

The switch in the top bar flips the whole application between a light and a dark appearance. Your
choice is remembered on that computer, and the first time you visit, Dashboard follows whatever your
operating system prefers.

## Your privacy, in short

- Your password is stored **hashed** — the application cannot read it, and neither can anyone with
  access to the database.
- API keys and the GitHub token are stored **encrypted**, and no page of the application ever shows
  them back, to you or to an administrator.
- An administrator can see that your account exists, when it was created, and how many widgets and
  services you have. They cannot see your credentials.
- Everything you configure is yours alone. Another user cannot reach your widgets, even knowing their
  identifiers.

## For administrators

One account can be designated the administrator, by the person who installs the application. An
administrator can list the accounts, delete one, and grant or revoke the administrator role.

Two things are deliberately impossible, as safety rails: an administrator cannot delete their own
account, and cannot remove their own administrator role. That guarantees the application can never
end up with nobody in charge.

Note also that a role change takes effect the next time the person logs in — up to an hour — because
of how the application remembers who you are.

There is **no administration screen yet**: these actions exist in the API and are used with a tool
like Postman. Whoever installed the application will know how.
