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

# Desktop app (Windows)
dotnet build Larmcentralen.App -t:Run -f net11.0-windows10.0.19041.0

# Tests
dotnet test --project tests/Larmcentralen.Client.Core.Tests
```

The app calls `https://localhost:7094/` by default. Point it elsewhere (for example the pilot
API on Azure) with the `LARMCENTRALEN_API_URL` environment variable, or by editing
`Larmcentralen.App/Resources/Raw/appsettings.json`.
