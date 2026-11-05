# Contributing to PlaceContext

Thanks for helping improve PlaceContext. Bug reports, documentation fixes, tests, and focused code
changes are all welcome.

## Before opening a change

1. Search existing issues and pull requests to avoid duplicate work.
2. Open an issue before a large architectural change so the approach can be discussed.
3. Keep changes focused; avoid mixing unrelated refactors into a bug fix.
4. Never commit credentials, private deployment addresses, customer data, or generated artifacts.

## Development setup

Install the .NET 10 SDK and `curl`, and start Docker, then run:

```bash
dotnet restore PlaceContext.slnx
./run.sh
```

The portal is served at `http://localhost:7700` and the MCP endpoint is `/mcp`.
If the local app is already healthy, the script prints "The application is already running."
and exits successfully before checking or starting dependencies.
The script checks the SDK and Docker, starts the existing `devcontext-validation-postgres`
container, and waits for a successful database query before launching the host. If the container
does not exist, it creates PostgreSQL 17 on `127.0.0.1:5433` with the defaults in
`appsettings.json` and a persistent `placecontext-dev-postgres` Docker volume.
The database keeps running when the app exits; existing database volumes are preserved.
The script forwards arguments to `dotnet run`, for example `./run.sh --no-build`.

## Repository layout

- `src/PlaceContext.Domain`: entities, value objects, domain services, and repository contracts.
- `src/PlaceContext.Application`: use cases grouped by feature, with commands, queries, handlers,
  DTOs, and services. Feature `Interfaces` folders contain infrastructure contracts; feature
  `Models` folders contain their request/result contracts. Shared helpers belong in `Shared`.
- `src/PlaceContext.Infrastructure`: adapters grouped by capability or feature.
  `Persistence` contains the context, with separate `Rows`, `Repositories`, `Stores`, `Queries`,
  and `Migrations` folders. All migrations and the model snapshot live in `Persistence/Migrations`;
  retain their existing namespaces and migration IDs. Use `--output-dir Persistence/Migrations`
  when adding migrations. `Workload` groups `Containers`, `Cluster`, `VirtualMachines`, and `Review` adapters,
  with shared options and runtime models separate. Feature folders separate models, options,
  interfaces, and persistence implementations where needed. `Scheduling` separates workers,
  queues, and persistence; `Communications` contains messaging and signed webhooks.
- `src/PlaceContext.Host`: web endpoints, components, and view models. Presentation models belong
  in `Components/Models`; feature-specific models can stay with their feature. Public HTTP request
  and response DTOs live in `Api/Contracts`, with translations in `Api/Mappers`. `Middleware` handles
  request identity/routing; `Components/State` and `Components/Circuits` hold Blazor state and circuit
  integration. `Mcp` contains the MCP tool/prompt adapters. `Auth` separates authentication,
  authorization, OAuth, SSO, options, models, and standalone pages/templates. Reusable backup archive
  generation belongs with `Application/Backup`, not in Host.
- `src/PlaceContext.DesktopAgent`: VM guest agent.
- `tests`: tests grouped by layer, plus browser and deployment checks.
- `deploy`, `examples`, and `docs`: deployment assets, runnable examples, and development notes.
  Operator documentation is embedded from `src/PlaceContext.Host/Wiki`.

Keep one top-level type per C# file, including interfaces, enums, structs, and records. Name the
file after its type; partial implementations may use `Type.Feature.cs`. Keep private implementation
types nested when they are only used by their enclosing type. Update callers when renaming CLR types; preserve HTTP routes, serialized discriminator values,
configuration keys, database table names, and migration identities for compatibility. Do not delete migrations
or compatibility code solely because a feature has disappeared from navigation.

## Design boundaries

Dependencies point inward: Host composes Application and Infrastructure; Infrastructure implements
contracts owned by Application or Domain; Application depends on Domain. Domain owns business rules
and invariants, with no dependency on the web framework, database, or external service clients.
Use the existing feature folders and CQRS dispatcher rather than adding another service layer.
The web host may reference Infrastructure only from `Program.cs` for composition. Endpoints and
view models call application services or ports; they do not use EF, persistence rows, repositories,
or units of work. Background workers and protocol clients live in Infrastructure. Build and agent
workflow orchestration, communications use cases, tenant context, and operation tracking live in Application.

Commands express state changes. Their handlers coordinate domain operations and commit through the
unit of work where needed. Queries return read models and do not own a write commit boundary.
Every command/query has one scoped handler; the dispatcher supplies the per-call scope and checks
declared permissions. Keep authorization and tenant isolation intact when refactoring either path.

Use EF Core queries for mapped application data in Infrastructure. Project directly to the required
read model, use no-tracking reads where changes are not needed, and keep filtering and pagination in
the database. Use parameterized SQL only when the operation requires it (such as user-authored SQL,
dynamic project schemas, or database administration), or when a representative benchmark demonstrates
a performance gain over an appropriate EF query. Document that evidence with the exception; do not
assume raw SQL is faster. Keep all database access behind Application or Domain contracts.

Treat project tables as the source of truth and search/vector indexes as derived representations.
New ingestion paths must declare how PII is classified, encrypted, authorized for processing, and
removed from derived data when the source changes or is deleted. Encryption at rest alone does not
authorize plaintext in logs, prompts, embedding requests, or job output. Keep encryption keys out
of jobs; decrypt only through an authorized application boundary. The assurance target is
[SOC 2 Type II](SECURITY.md#soc-2-type-ii-readiness). Do not claim compliance from code changes or
encryption tests: controls, evidence of operation over the examination period, and an independent
examination are separate requirements. Existing paths must be assessed before being reused for
sensitive data.

HTTP, database, filesystem, and process implementations belong in Infrastructure behind inward-owned
interfaces. Host API contracts describe the wire format; map them to application commands/read models
instead of exposing persistence rows or returning tracked domain objects. Separate API DTOs are useful
when they protect that public contract; avoid extra copies that serve no boundary.

Name shared interfaces for capabilities, such as outcome review, tenant runtime provisioning, and
billing-event processing. Adapters implement those interfaces and are selected in dependency injection.
Name owned classes and folders for capabilities (`Search`, `Payments`, `ObjectStorage`) rather than
vendors. Supported provider identifiers remain in configuration, wire protocols, and historical
migrations. Keep provider payloads, credentials/configuration formats, and protocol details in Infrastructure. Plain options and data models do not need marker interfaces; callers of a vendor
service should depend on its capability contract rather than its concrete implementation.

Architecture tests enforce dependency direction, transport isolation, handler registration, source
organization, and capability naming. They complement behavior tests; they cannot prove that a query's
entire call graph is free of side effects. Review that when changing query services or repositories.

LLM instructions and authoring guides live in feature `Prompts/*.md` files embedded in Application.
Load them with `PromptText`; render named parameters once so inserted user data is never interpreted
as a template. Keep tool schemas with their protocol contracts. The desktop agent embeds its
`prompts/*.md` with `include_str!`.

## Code style

Apply these rules to authored application code and tests. Use braces for control-flow bodies and
place each block and statement on its own line. Never put an `if`, loop, or another block on the same
line as its body. Prefer guard clauses: return, throw, or continue early instead of using `else`.
Extract a focused helper when branches must share work afterward; an early return must not skip
persistence, cleanup, or UI notifications. Simple value selection can use conditional or switch
expressions. These conventions apply to authored C#, browser JavaScript, and Rust. Generated
migrations, vendor libraries, Razor conditional markup, and deployment scripts retain their own conventions.

Name classes for their role and match filenames to their type. A class derived from `BackgroundService`
ends in `BackgroundService`; other `IHostedService` implementations end in `HostedService`. Controllers,
handlers, options, and interfaces retain their corresponding role names and established conventions.
The editor configuration and architecture tests enforce the C# rules.

## Checks

Run the checks relevant to your change before opening a pull request:

```bash
dotnet build PlaceContext.slnx
dotnet test PlaceContext.slnx
```

Add regression coverage for behavior changes. Update the embedded wiki under
`src/PlaceContext.Host/Wiki/` when user-facing behavior changes.

## Pull requests

Explain the problem and outcome, list the checks you ran, and call out migrations, configuration
changes, or compatibility concerns. By contributing, you agree that your contribution is licensed
under the repository's MIT License.

Pull requests require approval from a code owner before merge. Maintainers decide whether and when
a contribution is accepted; opening an issue or pull request does not guarantee inclusion. Direct
pushes, force pushes, and branch deletion are disabled on `main`.
