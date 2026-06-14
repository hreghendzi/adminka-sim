# adminka-sim

A deliberately **simple** simulation product for the Adminka platform — the
"casino" side of the FASTPAY merchant integration, split out of the `adminka`
platform repo so the two communicate **only over the network**.

> Planning + rationale: `docs/adminka-sim-separation-plan.md` in the
> `adminka-workspace` hub. Board milestone **m-10**.

## What it is

A single ASP.NET Core **Razor Pages** app (one app, one database) that plays an
external merchant:

- **3 demo users** (DB-backed ASP.NET Core Identity, cookie login — no Duende,
  no 2FA, no OIDC). Each user logs in and sees **their own wallet** and
  **deposit / withdraw** actions.
- **Own wallet ledger** — adminka-sim is the authority for its wallet balances.
  Deposits / withdrawals are driven through adminka's **merchant API** over
  HTTPS; the wallet is credited / debited only when adminka's **webhook
  callback** confirms (callback hash verified). No shared DB, no LAN read.

It knows the `adminka` platform **only** as a merchant gateway at a base URL,
authenticated by MID + hash. That boundary is what lets the future "no LAN
access" step be a config change, not a redesign.

## Tech

.NET 10 · ASP.NET Core Razor Pages · EF Core 10 + PostgreSQL (Npgsql) ·
Central Package Management. **No** references to the `Adminka.*` platform
projects; **no** DevExpress. Restores from nuget.org only.

## Layout

```
src/AdminkaSim.Web      — the Razor Pages app (auth, wallet UI, merchant client, /callback)
tests/AdminkaSim.Tests  — xUnit tests
```

## Run locally

Requires the .NET 10 SDK and a reachable PostgreSQL. Set the connection string
(`ConnectionStrings:DefaultConnection`) via user-secrets or environment, then:

```bash
dotnet run --project src/AdminkaSim.Web
```

Migrations are applied and the 3 demo users seeded on startup. Demo logins:
`player1@demo.local` / `player2@demo.local` / `player3@demo.local` (password
`Demo123` by default — override via `Seed:DemoUserPassword`).
