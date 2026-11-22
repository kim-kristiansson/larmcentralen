# Larmcentralen

Alarm knowledge base for operators at a heat treatment plant: search an alarm, read what it
means and follow a step-by-step solution. It is a documentation platform, not an alarm logger.

## Solution layout (`Larmcentralen/`)

| Project | What it is |
| --- | --- |
| `Larmcentralen.Domain` | Entities and repository interfaces. No dependencies. |
| `Larmcentralen.Application` | DTOs and services (search, export, sync snapshot). |
| `Larmcentralen.Infrastructure` | EF Core on SQLite, repositories, migrations. |
| `Larmcentralen.Api` | ASP.NET Core Web API, SharePoint and SMS integrations. |
| `Larmcentralen.Client.Core` | Client logic with no UI: HTTP API client, local SQLite copy, sync, offline search. |
| `Larmcentralen.Client.UI` | Every screen as Razor components (Swedish UI), plus the bundled Quill editor. |
| `Larmcentralen.App` | .NET MAUI Blazor Hybrid desktop app (Windows) that hosts `Client.UI`. |
| `tests/Larmcentralen.Client.Core.Tests` | Tests for search ranking, sync and the offline store. |

Everything targets **.NET 11**. The target framework is set once in `Directory.Build.props`
(`LarmTargetFramework`) and package versions once in `Directory.Packages.props`.

## How the client works

- On start-up the app opens its local SQLite file (`%LOCALAPPDATA%\…\larmcentralen.db`) and loads
  it into memory, so search and reading work at once, with or without a network.
- It then pulls a full snapshot from `GET /api/sync` and repeats every five minutes and whenever
  the network comes back. The top bar shows online/offline and when it last synced.
- Search runs in memory against the local copy: every typed term must match the start of a word
  in the alarm code, title, equipment (technical or everyday name), area or description.
  Alarm codes also match without their letter prefix (`200.4` finds `M200.4`).
- Creating, editing and deleting need a connection in v1. After each write the app re-syncs.
- Solutions are stored and edited as HTML with Quill 2 (MIT/BSD licensed, bundled locally).

## Server database

The API stores everything in one SQLite file, by default `Larmcentralen.Api/data/larmcentralen.db`
(set `ConnectionStrings:DefaultConnection`, for example `Data Source=D:\\Larmcentralen\\larmcentralen.db`).
On start-up the API creates the file if needed, applies migrations and turns on WAL mode.

- Keep the file on a local disk. Network shares (SMB, Azure Files, App Service `/home`) break
  SQLite's file locking.
- Back up with `sqlite3 larmcentralen.db ".backup backup.db"` or Litestream, not by copying the
  file while the API is running (the `-wal` file holds recent writes).
- Search is case-insensitive for å, ä and ö: the API replaces SQLite's ASCII-only `lower()` with
  .NET's on every connection (`Infrastructure/Data/SqliteSetup.cs`).

## Running locally

Requirements: .NET 11 SDK, and on Windows the MAUI workload
(`dotnet workload install maui-windows`).

```bash
cd Larmcentralen
# API (creates and migrates data/larmcentralen.db next to the API on start-up)
dotnet run --project Larmcentralen.Api --launch-profile https

# Desktop app (Windows): builds, then starts it. Close the running app first, or the build can't
# replace its files. (`dotnet build -t:Run` only starts the last build without rebuilding.)
dotnet run --project Larmcentralen.App -f net11.0-windows10.0.19041.0

# Tests
dotnet test --project tests/Larmcentralen.Client.Core.Tests
```

The app calls `https://localhost:7094/` by default. Point it elsewhere (for example the pilot
API on Azure) with the `LARMCENTRALEN_API_URL` environment variable, or by editing
`Larmcentralen.App/Resources/Raw/appsettings.json`.

## API keys and secrets

This repository is public. Real keys, passwords and phone numbers never go in a tracked file:
`appsettings.json` only holds placeholders, and `Larmcentralen.App/Resources/Raw/appsettings.json`
ships inside the app. Locally they live in user-secrets (stored outside the repo); on Azure in the
App Service's environment variables.

Everything under `/api` requires a key:

- The app sends a key in the `X-Api-Key` header on every call. There are two keys, and they must differ:
  - `Security:ApiKey` is the read key for operators (role User): search and read only.
  - `Security:AdminApiKey` is the admin key (role Admin): can also create, edit and delete.
    Every endpoint that changes data requires it.
- The 46elks SMS webhook can't send headers, so it uses its own secret in the URL:
  `https://<api-host>/api/webhooks/sms?secret=<Security:SmsWebhookSecret>`.

### Alarm SMS (46elks)

The plant's alarm system (OCSS) sends alarms by SMS to a 46elks number. 46elks posts each one to the
webhook above (set it as the number's `sms_url` in 46elks). The API acts only on SMS **from** one of the
alarm system's numbers **to** the 46elks number. Admins set both in the app under
Inställningar → SMS-larm (`GET/PUT /api/settings/sms`, admin key). Until both are set every SMS is turned
away. Numbers can be typed any Swedish way (`070-123 45 67`, `+46 70 123 45 67`) and are stored as `+4670…`.

An accepted SMS is matched to an alarm by unit and code (`… Line : T102 : M206.6 …`); for a Hög or
Kritisk alarm an alert SMS goes to `Email:AlertRecipient` through the 46elks account
(`Email:ElksUsername`, `Email:ElksPassword`). Leave those empty to receive without sending alerts. The
page shows the last SMS and what happened to it; nothing else is logged. The webhook always answers an
empty 200, because 46elks sends any text in the response back to the sender as a reply.

46elks must be able to reach the webhook from the internet over HTTPS, so the API needs a public address.

In Development the API runs without keys if none are set (it logs a warning). In any other
environment it refuses to start until all three (`Security:ApiKey`, `Security:AdminApiKey`,
`Security:SmsWebhookSecret`) are set. It also refuses to start if the two API keys are the same.

```bash
# Generate three keys (PowerShell: [Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 })))
openssl rand -base64 32

# Local API: store them in user-secrets
cd Larmcentralen/Larmcentralen.Api
dotnet user-secrets set "Security:ApiKey" "<read key>"
dotnet user-secrets set "Security:AdminApiKey" "<admin key>"
dotnet user-secrets set "Security:SmsWebhookSecret" "<webhook secret>"
dotnet user-secrets set "SharePoint:ClientSecret" "<secret>"      # and the other SharePoint values
dotnet user-secrets set "Email:ElksPassword" "<46elks password>"  # and ElksUsername, AlertRecipient
```

On Azure App Service, add the same settings as environment variables with `__` for `:`
(`Security__ApiKey`, `Security__AdminApiKey`, `Security__SmsWebhookSecret`, `SharePoint__ClientSecret`, ...).

Each PC running the app needs a key as a user environment variable, then a restart of the app:
the read key (`Security:ApiKey`) on operator PCs, the admin key (`Security:AdminApiKey`) on admin PCs.
The app asks the API for its role (`GET /api/me`), remembers it offline and hides edit features for users.

```powershell
setx LARMCENTRALEN_API_KEY "<read key or admin key>"
```

If a key leaks, generate a new one, update the API setting and `LARMCENTRALEN_API_KEY` on every PC
that uses that key.

A GitHub Actions check (`.github/workflows/secret-scan.yml`, gitleaks) fails any push or pull
request that adds something that looks like a secret. To catch it before committing, install
gitleaks (`winget install gitleaks`) and add `.git/hooks/pre-commit` containing
`gitleaks git --pre-commit --staged --redact -v`.

## Claude Code agents

`.claude/agents/` holds subagents for Claude Code, each on the cheapest model that does its job
well, so simple work does not run on the most expensive model.

| Agent | Model | Use it for |
| --- | --- | --- |
| `codebase-scout` | Haiku | Read-only lookups: where something lives, what touches it |
| `build-test-runner` | Haiku | Build and test, report only the failures |
| `swedish-ui-copy` | Haiku | Swedish text operators see in the app |
| `feature-developer` | Sonnet | Backend and client-logic features across the layers, migrations |
| `blazor-ui-developer` | Sonnet | Razor screens, components, Quill interop, MAUI host |
| `test-writer` | Sonnet | New and extended tests |
| `code-reviewer` | Sonnet | Read-only review of a diff before a PR |
| `architect` | Opus | Sync/offline design, data model, hosting, big refactors (plans, no code) |
| `debugger` | Opus | Hard bugs: sync, SQLite, EF Core, WebView/interop, CI-only failures |
| `principal-architect` | Fable | Expensive escalation only: hard-to-undo calls such as sync/conflict handling or data model changes (plans, no code) |

Claude picks one from its description, or ask for it by name ("use the architect agent to …").
To change an agent's model, edit the `model:` line in its file (`haiku`, `sonnet`, `opus` or
`fable`). Use `architect` for normal design work and escalate to `principal-architect` only
for decisions that are expensive to get wrong.
