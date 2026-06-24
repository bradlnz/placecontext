# PlaceContext — hosted MCP server + codebase-visibility portal

A **hosted MCP server** that is the system of record for *development context* across multiple
projects, paired with an embedded web **portal** (MCP-Inspector-style). Both surfaces run in one
process on `http://localhost:7700`: the portal at the site root and the MCP server over
**Streamable HTTP** at `/mcp`.

A coding agent (Claude Code) connects to `/mcp` and reads/writes context through the MCP tools
while developing features; you watch and steer from the portal. PlaceContext:

- **Registers projects on demand** through the MCP `create_project` tool (no folder scanning) and
  **graphifies** each into a decision tree.
- Routes **every change through a git-backed ledger** (what/why/author/agent-vs-human/
  test-delta/debt-delta → scoped commit → `graphify --update`).
- Measures **technical debt** and **agentic debt** and surfaces them on dashboards.

Built **Onion-first and test-first** (mirrors the CodeRag template): the inner layers go green in
dependency order **Domain → Application → Infrastructure → Host**. Domain, Application, and
Architecture suites are GREEN; the Host runs against PostgreSQL with the portal and MCP endpoint
live.

## Layout (dependencies point inward)

```
src/
  PlaceContext.Domain          → core; references nothing
  PlaceContext.Application     → CQRS dispatcher + handlers + ports
  PlaceContext.Infrastructure  → graphify ACL, git, EF Core/PostgreSQL, debt strategies, metrics
  PlaceContext.Host            → MCP (Streamable HTTP) tools + Blazor portal (composition root)
tests/
  PlaceContext.Domain.Tests        → GREEN (invariants, debt scorers)
  PlaceContext.Application.Tests   → GREEN (command/query handlers)
  PlaceContext.Infrastructure.Tests→ integration, Skip-ped until wired
  PlaceContext.Architecture.Tests  → GREEN (NetArchTest enforces inward-only deps)
  PlaceContext.TestSupport         → in-memory fakes + object mothers
```

## Prerequisites

- .NET 10 SDK (`dotnet --version`)
- [`graphify`](~/.claude/skills/graphify) on `PATH` (Python)
- A PostgreSQL instance matching `PlaceContext:ConnectionString` in `appsettings.json`. For local dev:

  ```bash
  docker run -d --name placecontext-db \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_USER=postgres -e POSTGRES_DB=placecontext \
    -p 5433:5432 postgres:16
  ```

## Run the baseline

```bash
dotnet restore
dotnet build
dotnet test            # Domain/Application/Architecture GREEN; Infra integration SKIP
dotnet run --project src/PlaceContext.Host   # portal at http://localhost:7700 + MCP over HTTP at /mcp
```

The schema is created automatically on startup (`EnsureCreated`). Point an MCP client at
`http://localhost:7700/mcp` (Streamable HTTP).
