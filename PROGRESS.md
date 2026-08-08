# PlaceContext Microservice Migration Progress

Last updated: 2026-08-08 (Australia/Brisbane)

## Target architecture

- Independently replaceable services: AgentChat, Agents, Artifacts, CRM, Data, Jobs (jobs, chains, schedules and triggers), Search (including OpenSearch), and Vault.
- Each service owns its contracts, domain, implementation, controllers, runtime, persistence adapters, and tests.
- Shared types live physically under `src/PlaceContext.Application`; low-level shared contracts are compiled by `PlaceContext.BuildingBlocks` to avoid circular dependencies.
- The React application lives in the root-level `frontend/` workspace. Its frontend-facing HTTP surface will move into a dedicated `PlaceContext.App` project; `PlaceContext.Host` remains the legacy Blazor host during the migration.
- Service APIs will have explicit route ownership such as `/api/jobs`, `/api/search`, `/api/data`, `/api/vault`, `/api/crm`, `/api/agent-chat`, and `/api/artifacts`.

## Completed and verified

- Split production C# source files so each has exactly one logical declaration at most, including nested classes, records, structs, interfaces, enums, and delegates.
- Reorganized role folders so checked folders contain matching types (`Services` → `*Service`, `Handlers` → `*Handler`, etc.).
- Created service implementation and nested contract projects for all eight services.
- Created service-owned domain projects for AgentChat, Agents, Artifacts, CRM, Data, Jobs, Search, and Vault.
- Moved BuildingBlocks physically to `src/PlaceContext.Application/BuildingBlocks`.
- Moved the shared `PostJobActionKind` type into Application BuildingBlocks because both Jobs and Artifacts consume it.
- Added all extracted projects to `PlaceContext.slnx` and wired module registration into the current host.
- Removed the unused Cost feature end-to-end from Application, Domain, Infrastructure, MCP tools, and tests.
- Removed Infrastructure's concrete dependency on the Search `DecisionTreeProvider` by adding the shared `IUncachedDecisionTreeProvider` seam.
- Added architecture tests that enforce:
  - at most one declared type per production source file, including nested types;
  - filename/type-name alignment (including partial and EF migration conventions);
  - immediate role-folder/type suffix alignment;
  - no service implementation project references another service implementation project.
- Fixed existing nullable project/vault call sites so the host builds again.
- Created one test project per service and moved bounded-context tests out of the central Application suite.
- Moved semantic run-output search (`SearchRunOutputsQuery`, DTO, and handler) from Jobs to Search.
- Added service-owned ASP.NET controllers and stable route prefixes for all eight services; the current gateway discovers them as MVC application parts.
- Added independently executable `.Api` runtime projects for AgentChat, Artifacts, CRM, Data, Jobs, Search, and Vault.
- Added shared `PlaceContext.ServiceDefaults` under Application with JWT validation, permission-claim policies, authenticated tenant/user context propagation, controller discovery, and `/health` endpoints.
- Split monolithic composition into `AddApplicationCore`/`AddInfrastructureCore` for service runtimes while retaining the full gateway composition methods.
- Added API-surface-only DI composition per service so a runtime does not activate unrelated bounded-context handlers.
- Removed Jobs' direct `IChatCommandRepository` dependency; creation of the old command-trigger mode now fails explicitly because its scheduler path never executed command triggers.
- Extracted Vault persistence/encryption adapters into `PlaceContext.Vault.Infrastructure`, moved their DI registration to `AddVaultInfrastructure`, and composed that module from the gateway.
- Gave Vault its own `VaultDbContext`, persistence row, design-time factory, migration assembly, `__EFMigrationsHistory_Vault` history table, connection configuration, health check, encryption bootstrap, and `IVaultUnitOfWork` boundary.
- Removed Vault's `job_secrets` model and encryption scan from the shared `AppDbContext`; the Vault runtime and infrastructure no longer reference the shared Infrastructure implementation project.
- Added the Vault-owned initial migration with safe adoption of the legacy `job_secrets` table and confirmed that both Vault and shared EF models have no pending model changes.
- Added a shared handoff migration that removes the unused `usage_records` table and retains the required CRM client/job-chain assignment table without recreating Vault ownership in the shared model.
- Moved generic standalone-runtime tenant, user, and clock implementations under Application runtime composition so service APIs do not need shared Infrastructure just to establish request context.
- Added Vault persistence tenant-isolation and data-protection compatibility tests; shared Infrastructure tests now use the Vault repository contract rather than the Vault EF implementation.
- Gave AgentChat its own `AgentChatDbContext`, four owned rows, `IAgentChatUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_AgentChat` history table, and non-destructive initial migration for legacy gateway data.
- Removed `agent_configs`, `agent_chat_sessions`, `mcp_connections`, and `chat_commands` from the shared `AppDbContext`; the shared handoff migration records ownership transfer without dropping those tables.
- Rewired AgentChat handlers and shared MCP command handlers to the AgentChat-specific commit boundary so they cannot commit unrelated shared persistence.
- Removed shared Infrastructure project references from both `PlaceContext.AgentChat.Infrastructure` and `PlaceContext.AgentChat.Api`; the standalone runtime now composes its own persistence directly.
- Added AgentChat tenant-isolation and four-repository round-trip coverage, plus an architecture check that locks its context, rows, unit of work, migration snapshot, and project boundaries to the service.
- Gave Artifacts its own `ArtifactsDbContext`, two owned rows, `IArtifactsUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_Artifacts` history table, and non-destructive initial migration for legacy gateway data.
- Removed `job_run_artifacts` and `artifact_share_tokens` from the shared `AppDbContext`; the shared handoff migration records ownership transfer without dropping either table.
- Rewired artifact handlers and the shared post-job action service to the Artifacts-specific commit boundary, and removed shared Infrastructure references from the Artifacts API and infrastructure projects.
- Kept the shared encryption contract in Application while placing concrete ASP.NET Data Protection adapters in each runtime infrastructure layer, preserving compatibility without introducing framework dependencies into Application.
- Added Artifacts persistence tenant-isolation coverage and an architecture check that locks its context, rows, unit of work, migration snapshot, and project boundaries to the service.
- Gave Search its own `SearchDbContext`, dashboard row, `ISearchUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_Search` history table, and non-destructive initial migration for legacy gateway data.
- Removed `opensearch_dashboards` from the shared `AppDbContext`; the shared handoff migration records ownership transfer without dropping the table.
- Rewired the OpenSearch dashboard store to Search-owned persistence and a runtime-local data-protection adapter, and removed shared Infrastructure references from the Search API and infrastructure projects.
- Added Search dashboard tenant-isolation/encryption round-trip coverage and an architecture check that locks its context, row, unit of work, migration snapshot, and project boundaries to the service.
- Gave Data its own `DataDbContext`, six owned rows, `IDataUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_Data` history table, and non-destructive initial migration for legacy gateway data.
- Moved mappings, entities, entity tags, record links, charts, and saved SQL queries out of the shared `AppDbContext`; the shared handoff migration releases all six tables without dropping them.
- Rewired Data repositories, handlers, chart generation, project-database resolution, JSON flattening, and legacy chart encryption to Data-owned persistence and runtime configuration.
- Removed shared Infrastructure references from the Data API and infrastructure projects, and changed tenant-sensitive entity/mapping mutations from `FindAsync` to query-filtered lookups.
- Added Data tenant-isolation and saved-query round-trip coverage plus an architecture check that locks the context, rows, unit of work, migration snapshot, and project boundaries to the service.
- Gave Jobs its own `JobsDbContext`, nine owned rows, `IJobsUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_Jobs` history table, and non-destructive initial migration for legacy gateway data.
- Moved jobs, runs, test cases, triggers/schedules, chains, chain runs, event definitions/occurrences, and the durable pending-run queue out of the shared `AppDbContext`; the shared handoff migration releases all nine tables without dropping them.
- Rewired Jobs repositories, queueing, schedule scanning, event dispatch, continuation/status workers, and command handlers to the Jobs-specific commit boundary; shared activity workflows now raise events through `IEventDispatcher` rather than a concrete Jobs service.
- Moved schedule/event implementations out of the shared Application source tree into Jobs, and moved legacy Jobs/chain/event encryption plus hot run indexes into the Jobs database lifecycle.
- Added Jobs tenant-isolation, global queue, legacy encryption, and CRM/Jobs cross-boundary persistence coverage plus an architecture check that locks Jobs persistence ownership to the service.
- Gave CRM its own `CrmDbContext`, eleven owned rows, `ICrmUnitOfWork`, connection configuration, health check, `__EFMigrationsHistory_Crm` history table, and non-destructive initial migration for legacy gateway data.
- Moved clients, CRM job/chain run links, communications, appointments, calendars, client artifacts/chain assignments, automation rules/queue, and ingestion settings out of the shared `AppDbContext`; the shared handoff migration releases all eleven tables without dropping them.
- Rewired CRM repositories, handlers, ingestion, workers, encryption bootstrap, and the public ingestion controller to CRM-owned persistence while keeping tenant/project lookup as an explicit transitional platform dependency.
- Added CRM tenant-isolation/global-queue coverage and a sixteenth architecture check that locks its context, rows, unit of work, and migration snapshot to the service.
- Added shared `ITenantCatalog` and `IBackgroundOperationNotifier` ports under Application, with platform EF/operation-center adapters in shared Infrastructure.
- Rewired Jobs and CRM cross-tenant workers to `ITenantCatalog`/`ICurrentTenantAccessor`, rewired Jobs progress publication to the operation-notification port, and changed CRM ingestion project validation to the project repository boundary.
- Removed the concrete `PlaceContext.Infrastructure` project reference from both Jobs and CRM implementation projects; each now builds independently and owns a wire-compatible Data Protection encryptor for its runtime.
- Completed Agents persistence ownership with its four tenant-owned rows, `AgentsDbContext`, `IAgentsUnitOfWork`, `__EFMigrationsHistory_Agents` history table, safe initial migration, configured connection option, tenant-isolation/repository tests, and architecture ownership check.
- Removed the unused Host `BrowseTab` enum and moved controller/view-model records into matching `Records` folders.
- Aligned Vault infrastructure's EF Core/Relational dependencies at 10.0.9 so the extracted project builds without assembly-conflict warnings.
- Extracted OpenSearch connection/data/sync gateways and the dashboard persistence adapter into `PlaceContext.Search.Infrastructure`; the gateway now composes them through `AddSearchInfrastructure`.
- Moved OpenSearch adapter tests from the shared Infrastructure suite to Search and replaced their concrete Vault/EF setup with a contract-level secret repository stub.
- Moved shared OpenSearch environment-variable names into Application so the host UI and Search resolver no longer couple through a concrete adapter.
- Removed three stale OpenSearch export tests whose `ExportIndexAsync` API had no production interface, implementation, or caller.
- Fixed OpenSearch SQL truncation detection to use `row_count`/cursor rather than document-match `total`.
- Extracted project-database access, JSON flattening, Data entity/mapping/tag/link repositories, and chart persistence into `PlaceContext.Data.Infrastructure` with `AddDataInfrastructure` composition.
- Moved project-database environment-variable names into Application so the host settings UI does not depend on Data's concrete resolver.
- Moved project-data paging/provisioning and JSON-flattening tests from shared Infrastructure into the Data test project.
- Extracted Jobs repositories, durable run queue, trigger/chain/status workers, workload runners/scripts, run caches, and telemetry into `PlaceContext.Jobs.Infrastructure` with `AddJobsInfrastructure` composition.
- Moved Jobs/workload/scheduler/persistence adapter tests from shared Infrastructure into the Jobs test project and fixed resource loading for the new assembly-owned script path.
- Extracted CRM repositories, ingestion settings, durable automation queue, and CRM workers into `PlaceContext.Crm.Infrastructure` with `AddCrmInfrastructure` composition.
- Moved CRM encryption/ingestion adapter tests from shared Infrastructure into the CRM test project.
- Extracted Artifacts object storage, artifact link/share-token persistence, and document extraction into `PlaceContext.Artifacts.Infrastructure` with `AddArtifactsInfrastructure` composition.
- Moved Artifacts storage/share/document tests and their PDF fixture into the Artifacts test project.
- Extracted AgentChat chat gateways, Slack adapters, chat-memory stores, and agent/MCP persistence into `PlaceContext.AgentChat.Infrastructure` with `AddAgentChatInfrastructure` composition.
- Removed shared Infrastructure's project/package dependency on the AgentChat implementation and moved Slack signature tests into AgentChat.
- Replaced Jobs' concrete dependency on AgentChat's `AgentSessionRunner` with the shared `ILaunchpadRunner` Application port; Jobs and AgentChat no longer reference each other's implementation projects.
- Removed now-unused AWSSDK, Redis, Cronos, PdfPig, and workload-resource references from the shared Infrastructure project; those dependencies now live with their owning services.
- Removed stale tests and unreachable icon cases for legacy `data.*` menu entries after confirming the current catalog exposes the consolidated `data` route only.
- Removed five test-analyzer warnings, including two accidentally duplicated public helper methods left in outer test classes.
- Isolated the process-global Jobs telemetry listener tests from parallel activity emitters, removing a full-suite race that individual project runs did not expose.
- Updated the Host authentication handler to the current `TimeProvider`-based framework API, guarded a nullable CRM portal response, and converted Blazor parameters to analyzer-compliant auto-properties.
- Removed empty source folders left behind by the physical moves.
- Preserved the existing root `frontend/` React 19/Vite workspace and its first Workspace Overview vertical slice; it is mounted at `/app` while the Blazor portal remains available during migration.
- Migrated the dashboard, identity entry points, About page, and the Branding, API Tokens, Artifact Filters, Backup, Communications, Connections, Locality, and Menu settings routes into route-split React vertical slices backed by canonical `/api/v1` contracts.
- Added the Connections settings API and React page for encrypted per-project Postgres and OpenSearch configuration without exposing saved credential values to the browser.
- Reviewed the referenced .NET architecture guidance. It supports bounded contexts, dependency inversion, aggregate boundaries, and domain events, but does not require concrete aggregate type names to end in `AggregateRoot`; concrete types retain their ubiquitous-language names while filenames continue to match types.

## Current verification state

- `PlaceContext.Application` builds with 0 warnings and 0 errors after domain extraction.
- Data, Jobs, Search, and Vault implementation projects build independently with 0 warnings and 0 errors.
- `PlaceContext.Host` builds with 0 warnings and 0 errors.
- `PlaceContext.Vault.Infrastructure` builds with 0 warnings and 0 errors, and the gateway builds successfully with the extracted module.
- `PlaceContext.Search.Infrastructure` builds with 0 warnings and 0 errors.
- `PlaceContext.Data.Infrastructure` builds with 0 warnings and 0 errors.
- `PlaceContext.Jobs.Infrastructure` builds with 0 warnings and 0 errors.
- `PlaceContext.Crm.Infrastructure` builds with 0 warnings and 0 errors.
- `PlaceContext.Artifacts.Infrastructure` builds with 0 warnings and 0 errors.
- `PlaceContext.AgentChat.Infrastructure` builds with 0 warnings and 0 errors.
- Full `PlaceContext.slnx` build: 0 warnings and 0 errors.
- All eight API runtime projects build independently; the previously exercised runtime validation remains green, Jobs reached a listening Kestrel endpoint, and Vault `/health` returned HTTP 200 `Healthy`.
- Jobs, CRM, and Agents persistence ownership architecture coverage is passing. The live production tree, including the current uncommitted Host BFF/frontend-migration controllers and records, passes all 17 architecture tests.
- Service test projects passing independently:
  - AgentChat: 42 tests
  - Agents: 6 tests
  - Artifacts: 57 tests
  - CRM: 20 tests
  - Data: 169 passing, 1 integration test skipped
  - Jobs: 210 passing, 5 Docker integration tests skipped
  - Search: 34 tests
  - Vault: 4 tests
- Clean full architecture suite: 17/17 passing, including Jobs, CRM, and Agents persistence/dependency ownership checks.
- Vault tests are 4/4 passing after its database and encryption lifecycle extraction.
- Search tests are 34/34 passing after its owned dashboard database and tenant-isolation move.
- Data tests are 169/169 passing with one local-Postgres integration test skipped after its owned database and tenant-isolation move.
- Jobs tests are 210/210 passing with five Docker integration tests skipped after its owned database, encryption, and tenant-isolation move.
- CRM tests are 20/20 passing after its owned database, encryption, and tenant-isolation move.
- Artifacts tests are 57/57 passing after its owned database and tenant-isolation move.
- AgentChat tests are 42/42 passing after its owned database, unit-of-work, and repository-test move.
- Shared Infrastructure tests: 97/97 passing, including the platform tenant-catalog adapter.
- Complete clean staged snapshot: 891 passed, 6 environment-dependent integration tests skipped, 0 failed.
- Artifacts presigned-URL coverage now allows the AWS SDK's two-second signing-clock tolerance instead of intermittently requiring an exact expiry second.
- React frontend: lint passes with zero warnings, 46 test files/89 tests pass, and a production build succeeds when emitted to an isolated output directory.
- Host tests now compile against explicit Artifacts, Data, and Jobs contract/domain project references instead of relying on transitive service assemblies. The current run is 285/289 passing; stale source-path, response-shape, authentication-message, and JSON-format assertions have been reconciled.

## In progress

- Organize the current React-migration BFF surface: 58 newly added Host request/response records have been split into filename-matched files, `JobTestPageMapper` moved from `Controllers` to `Controllers/Api/Mappers`, and the nested `OptionalLoad` helper moved to its own record file. The live production source-organization suite is 17/17 passing and the Host builds with zero warnings/errors.
- Complete the remaining four Host contract-test fixes. `ProjectData.razor` now contains only its parameter/lifecycle glue, injects only `ProjectDataViewModel`, and no longer declares its table-tab class, chart record, or enums; those types each live in filename-matched files and SQL Studio behavior is isolated in `ProjectDataViewModel.Studio.cs`. Two SQL Studio contract assertions need updating, while `DataGraph` and `JobChains` still have page-owned state to move into their view models.
- Create `PlaceContext.App` as the dedicated backend for the root `frontend/` React workspace, then move the React/BFF controllers, mappers, and request/response records out of `PlaceContext.Host` without mixing UI code into the app gateway project.
- Continue reducing the shared `AppDbContext`, row model, and migration history; Vault, AgentChat, Agents, Artifacts, Search dashboards, Data, Jobs, and CRM persistence ownership are complete, leaving shared platform tables and explicit platform-service seams.
- Decide explicit ownership for the remaining platform capabilities in shared Infrastructure, especially authentication/tenancy, communications, embeddings/vector storage, cluster integration, and analytics refresh.

## Remaining coupling to remove

- Service implementations still reference the main Application assembly as an extraction seam.
- The current host still references service implementations directly.
- All service implementation projects are free of concrete `PlaceContext.Infrastructure` project references. The Jobs and CRM executable API projects still compose shared Infrastructure for platform tenant/project adapters until those adapters are exposed by a dedicated platform/auth service client.
- The shared database model and migration history still contain tables for the other services and platform capabilities, preventing their independent database evolution.
- Standalone Data/Search runtimes currently fall back to service configuration when Vault adapters are absent; replace this transition seam with authenticated service-to-service Vault contracts.
- API runtimes currently expose the explicitly migrated controller surface; remaining gateway-only commands must move behind their owning service APIs before the gateway can drop implementation references.
- Search's decision-tree read model consumes Jobs, Data, and Artifacts domain models transitively; replace these with service contracts/read models.
- The extracted service runtimes need integration-event/outbox wiring; Vault, AgentChat, Agents, Artifacts, Search, Data, Jobs, and CRM now own their database migration and health-check paths.
- Contracts retain legacy `PlaceContext.Application.*` namespaces for source compatibility; physical ownership is correct, namespace cleanup remains.

## Next steps

1. Introduce the platform/auth service contract and client adapters so the Jobs and CRM executable API projects can stop composing shared Infrastructure.
2. Expand each service controller to cover its remaining gateway-only commands and replace CRM-to-Jobs/shared repository reads with HTTP/integration-event contracts.
3. Remove direct service implementation references from the gateway in favor of HTTP clients/integration events.
4. Continue building the React application in the root `frontend/` folder one vertical slice at a time, backed by `PlaceContext.App` and independently owned microservice routes.
5. Apply the referenced .NET architecture guidance per microservice while retaining ubiquitous-language aggregate names and filename/type alignment.
6. Continue reducing `PlaceContext.Infrastructure` to shared platform primitives only.

---

## React portal migration handoff — 2026-08-08

### Goal and delivery rule

- Migrate every route declared in `PlaceContext.Host/Components/Pages` to the React application in `frontend/`.
- TypeScript and TSX must be normally formatted and readable; do not leave generated-looking one-line source.
- Do not commit or push until every catalog route is migrated and the final frontend/Host verification passes.
- When complete, intentionally stage only the React-migration changes, commit, and push branch `agent/compact-portal-workspace`.

### React routes completed

The migration catalog is `frontend/src/app/host-route-catalog.ts`. These routes are marked `migrated` and have React pages plus React-facing Host APIs where required:

- Dashboard, Workspace Overview, MCP Inspector, Project overview
- Identity: Login, Setup, Onboarding
- System: About
- Settings: Access, API tokens, Artifact filters, Backup, Branding, Communications, Connections, Locality, MCP, Menu
- Data: Analytics, Data Graph
- Security: Vault / project secrets
- Collaboration: Wiki index and article
- Operations: Cluster
- Automation: Schedules, Jobs, multi-file Job editor, Tests, multi-file Test editor, Job Chains
- Events: project Events page is now wired into the router and marked migrated, but its newest changes have not yet been compiled or tested (see current partial work)

Important implementation additions include:

- Shared host Monaco loader at `frontend/src/shared/editor/load-host-monaco.ts`
- Shared graph support under `frontend/src/shared/graph/`
- React-facing controllers under `src/PlaceContext.Host/Controllers/` and response/request records under `Controllers/Api/Records/`
- Matching controller authorization coverage in `tests/PlaceContext.Host.Tests/SectionAuthorizationTests.cs`

### Current partial work

Events was interrupted by the previous Codex crash and has now been wired up:

- Backend: `EventsPageController.cs` and Events page records
- Frontend: `frontend/src/domains/events/`
- Route: `project/:projectId/events` in `frontend/src/app/router.tsx`
- Catalog status changed to `migrated`
- Event page styles appended to `frontend/src/styles/global.css`
- The Events TSX is still densely laid out because formatting was deliberately deferred until the functional migration is complete. Run Prettier before final verification.
- Add a focused Events UI test and compile this slice before treating it as verified.

Work began on a shared backend for Data Map and Entities:

- New unverified file: `src/PlaceContext.Host/Controllers/ProjectDataAdminController.cs`
- It exposes the mappings/jobs/tables/entities/link-groups page model plus mapping CRUD, entity CRUD, and linked-value rescan.
- Authorization coverage was added for `Permission.DataRead`.
- This file currently contains several public request/response record declarations alongside the controller. Before building, split each record into its own matching file under `Controllers/Api/Records/` to satisfy the repository's one-declaration-per-file architecture tests.
- No React Data Map or Entities screen has been created against this API yet.

### Routes still marked planned

Nine catalog entries remain:

1. CRM — `/project/:projectId/crm`
2. Project data — `/project/:projectId/data`
3. Entities — `/project/:projectId/entities`
4. Entity browse — `/project/:projectId/entity/:entityName`
5. Data map — `/project/:projectId/datamap`
6. Data search — `/project/:projectId/data-search`
7. Chat — `/chat`
8. Artifacts — `/artifacts`
9. Observability — `/observability`

Confirm with:

```bash
rg -n "status: 'planned'" frontend/src/app/host-route-catalog.ts
```

Suggested order is Data Map → Entities → Entity browse → Project data → Data Search → Events verification → Chat → Artifacts → Observability → CRM. The first five can reuse a coherent data-domain API/model layer.

### Formatting/readability guard

Prettier was installed as a frontend dev dependency and these scripts were added to `frontend/package.json`:

```bash
npm run format
npm run format:check
```

Configuration is in `frontend/.prettierrc.json` (`printWidth: 100`, no semicolons, single quotes, trailing commas). The entire existing frontend was formatted once, which explains the broad formatting-only TS/TSX diff. New Events code and anything added after it still need the final formatting pass. The user explicitly requested that all TypeScript remain readable.

### Last known verification

Before the latest Events and `ProjectDataAdminController` edits:

- `npm run lint` passed.
- Frontend production build passed to an isolated `/tmp` output directory.
- Focused UI tests passed for prior migrated slices; Tests has three focused tests and Jobs has two.
- `dotnet build src/PlaceContext.Host/PlaceContext.Host.csproj --no-restore` passed with zero errors after fixing the extracted `ProjectChartView` namespace import.

The Events wiring and new data-admin controller are unverified. Resume by splitting the new controller records, then run a Host build and frontend lint/build before expanding further.

### Final verification and publishing checklist

After zero routes remain `planned`:

```bash
cd frontend
npm run format
npm run format:check
npm run lint
npm test -- --run
npm run build -- --outDir /tmp/placecontext-react-final-build
cd ..
dotnet build src/PlaceContext.Host/PlaceContext.Host.csproj --no-restore
dotnet test tests/PlaceContext.Host.Tests/PlaceContext.Host.Tests.csproj --no-build
```

Also run the catalog test and architecture/source-organization tests if the focused Host test command does not include them.

Review `git status` and `git diff` carefully before staging. This worktree also contains the broader microservice extraction, so do not blindly use `git add .`; stage the intended frontend migration files and their Host BFF/test support explicitly. Nothing from this React migration checkpoint has been committed or pushed yet.

### Incremental React migration log

- 2026-08-08 — Stabilized the partial Events slice: router entry and styles are present, Events is marked migrated, frontend lint issues were corrected, and the production frontend build succeeded with the Events chunk included. A focused Events UI test is still pending.
- 2026-08-08 — Completed the Data Map and Entities React slices. Added the shared `ProjectDataAdminController` and split all page request/response records into architecture-compliant files. The React pages support mapping CRUD, entity CRUD, relation/tag editing, linked-value rescans, summary/catalog views, and migrated Data Tabs navigation. Frontend lint and a production build passed after these changes.
- 2026-08-08 — Added the Entity Browse React slice and `EntityBrowsePageController`. It supports paginated/searchable entity rows, typed create/edit/delete forms, record detail, auto-linked record discovery, and navigation to the migrated Graph and Analytics tools. The Host build passes with 0 warnings and 0 errors. Frontend lint/build for this newest slice is the immediate next check.
- Route catalog after the Entity Browse change: six routes remain planned — CRM, Project Data, Data Search, Chat, Artifacts, and Observability.
