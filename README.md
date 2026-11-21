# Larmcentralen

Alarm knowledge base for operators at a heat treatment plant: search an alarm, read what it
means and follow a step-by-step solution. It is a documentation platform, not an alarm logger.

## Solution layout (`Larmcentralen/`)

| Project | What it is |
| --- | --- |
| `Larmcentralen.Domain` | Entities and repository interfaces. No dependencies. |
| `Larmcentralen.Application` | DTOs and services (search, export, sync snapshot). |
| `Larmcentralen.Infrastructure` | EF Core on PostgreSQL, repositories, migrations. |
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

## Running locally

Requirements: .NET 11 SDK, PostgreSQL, and on Windows the MAUI workload
(`dotnet workload install maui-windows`).

```bash
cd Larmcentralen
# API (put the real connection string in appsettings.Development.json, which git ignores)
dotnet run --project Larmcentralen.Api --launch-profile https

# Desktop app (Windows)
dotnet build Larmcentralen.App -t:Run -f net11.0-windows10.0.19041.0

# Tests
dotnet test --project tests/Larmcentralen.Client.Core.Tests
```

The app calls `https://localhost:7094/` by default. Point it elsewhere (for example the pilot
API on Azure) with the `LARMCENTRALEN_API_URL` environment variable, or by editing
`Larmcentralen.App/Resources/Raw/appsettings.json`.

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
