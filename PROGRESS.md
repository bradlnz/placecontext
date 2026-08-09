# PlaceContext Microservice Migration Progress

Last updated: 2026-08-09 (Australia/Brisbane)

## Target architecture

- Independently replaceable services: AgentChat, Agents, Artifacts, Communications, CRM, Data,
  Identity, Jobs (jobs, chains, schedules and triggers), MCP, Operations, Projects, Search
  (including OpenSearch), Settings, Vault, and the React/App edge.
- Each service owns its contracts, domain, implementation, controllers, runtime, persistence adapters, and tests.
- Shared kernel and service-runtime projects now live at `src/PlaceContext.BuildingBlocks` and
  `src/PlaceContext.ServiceDefaults`; executable services no longer depend on Application for
  authentication, request context, controller discovery, or Kubernetes health endpoints.
- The React application lives in the root-level `frontend/` workspace. Its frontend-facing HTTP surface will move into a dedicated `PlaceContext.App` project; `PlaceContext.Host` remains the legacy Blazor host during the migration.
- Service APIs will have explicit route ownership such as `/api/jobs`, `/api/search`, `/api/data`, `/api/vault`, `/api/crm`, `/api/agent-chat`, and `/api/artifacts`.

## Completed and verified

- Split production C# source files so each has exactly one logical declaration at most, including nested classes, records, structs, interfaces, enums, and delegates.
- Reorganized role folders so checked folders contain matching types (`Services` → `*Service`, `Handlers` → `*Handler`, etc.).
- Created service implementation and nested contract projects for all eight services.
- Created service-owned domain projects for AgentChat, Agents, Artifacts, CRM, Data, Jobs, Search, and Vault.
- Moved BuildingBlocks physically to `src/PlaceContext.BuildingBlocks`, including the neutral CQRS,
  permission, tenant/user context, clock, and `PostJobActionKind` types shared by service boundaries.
- Moved ServiceDefaults physically to `src/PlaceContext.ServiceDefaults` and removed its dependency
  on the main Application assembly.
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
- Added top-level `PlaceContext.ServiceDefaults` with JWT validation, permission-claim policies,
  authenticated tenant/user context propagation, controller discovery, and `/health` endpoints.
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

- Finish the interrupted `PlaceContext.Application` extraction without restoring feature code to
  Application. Existing user moves are treated as intentional and completed in place.
- `PlaceContext.Identity` now owns login/setup/logout, identity context, 2FA, access/role settings,
  API tokens, and MCP OAuth. App routes Identity cookie traffic directly to that service.
- `PlaceContext.Mcp` now owns MCP connections, MCP execution, OAuth token persistence, its database
  context/migration history, and the former Host MCP controllers.
- `PlaceContext.Projects` is being introduced for project registry/context capabilities that do not
  belong in Identity or Data: projects, onboarding, requirements, activity, decisions, focus,
  workspace rollups, and improvement suggestions.
- `PlaceContext.Communications` and `PlaceContext.Settings` now have independent executable service
  scaffolds and App routes. Communications will own provider/email/SMS delivery configuration;
  Settings will own branding, locality, menu, backup settings, and connection configuration while
  calling Data/Search/Vault over HTTP for owner-specific mutations.
- `PlaceContext.Operations` now has an independent executable service scaffold and App route. It will
  own backup/restore execution, inspector, and operational administration after their Host controllers
  and adapters are moved.
- Data feature files moved out of Application are being split correctly between
  `PlaceContext.Data.Contracts` (requests/read models/ports) and the Data implementation project
  (handlers/services). The initial incorrect placement of implementations under `Data/Contracts`
  has been corrected.
- The remaining Application slices are queued by owner: Access/Auth/Membership to Identity;
  ChatCommands to AgentChat; Cluster/Skills to Agents; MCP commands/handlers/client to MCP;
  saved-query/data/graph code to Data; job telemetry to Jobs; and backup orchestration to its final
  cross-service boundary.
- Keep the two cluster concepts separate: `PlaceContext.Agents` owns the agent/fleet cluster
  (node inventory, join tokens, master promotion, Tailscale/k3s), while `PlaceContext.Jobs` owns the
  job cluster (workers, queues, schedules, executions, shards, and shard telemetry).
- No microservice may project-reference another microservice, including its Contracts assembly.
  Cross-service operations use authenticated HTTP with caller-local wire DTOs; shared kernel/runtime
  projects are the only allowed compile-time dependencies.
- Every executable service owns its own `/health` endpoint for Kubernetes probes. Host aggregate
  health is not migrated into a service; an optional multi-service status view belongs at the App
  edge and calls those health endpoints over HTTP.
- After Application extraction, migrate every remaining Host controller to its service or to the
  read-composition App edge, then remove obsolete Host records/auth/wiring and finally remove Host.
- Core API compatibility is retired rather than migrated: `/api/core/*`, its frontend-client auth,
  scopes, resource resolver, and Core DTOs have been deleted. Customer Portal configuration now uses
  CRM/customer-portal names instead of the retired Core API environment-variable names.
- Host currently has 24 controllers remaining. Ownership queue: Data (6), Projects (2), Search (1),
  Jobs (0 after Core API retirement), CRM/customer portal (4), Artifacts/OCR (1), split settings (3),
  App-edge composition/static routes (3), Backup/operations/inspector (3), and old Host health (1).

### Recovery verification checkpoint — 2026-08-09

- Branch: `agent/compact-portal-workspace`; this recovery work is being preserved in a checkpoint
  commit before the remaining controller and Application extraction continues.
- React frontend lint and production build pass after the Identity/MCP route changes.
- Before the concurrent Application cleanup, Identity API, MCP API, App, and Host builds passed;
  Identity tests were 1/1, MCP tests 2/2, and AgentChat tests 42/42.
- The main Application project builds again after completing the interrupted Agents cluster,
  AgentChat command, MCP command/client, and Jobs telemetry ownership moves. Executable service
  builds are the current verification focus; the full solution is not green yet.
- Focused builds currently pass for App, Jobs, Data, MCP, AgentChat, Agents, and Vault. Jobs-to-Data
  result enrichment and Agents-to-Vault secret resolution now cross authenticated HTTP boundaries;
  neither caller project-references the callee service.
- BuildingBlocks, ServiceDefaults, Communications API, Settings API, and Operations API build with
  0 warnings and 0 errors after the shared-runtime extraction. The three new services each inherit
  an independently owned `/health` endpoint from ServiceDefaults.
- Identity now owns caller-local MCP OAuth wire DTOs and no longer project-references MCP. The
  Identity API builds with 0 warnings and 0 errors after restoring the updated graph.
- Removed the Projects/Data project-reference cycle: graph rebuild now returns a Data-owned result,
  and the transitional Application facade maps it to the legacy project summary at its own boundary.
- Do not treat the older green full-solution snapshot above as the current working-tree result;
  it remains the pre-extraction baseline for regression comparison.

### Executable-boundary checkpoint — 2026-08-09

- The neutral dispatcher and `AddPlaceContextCqrs` composition now live in
  `PlaceContext.BuildingBlocks`. Shared encryption, storage, text-extraction, embedding/content,
  tenant-catalog, unit-of-work, HTML, and tool-observability contracts have also moved out of
  Application to their neutral or owning projects.
- Data no longer references Application. Its Jobs catalog/run reads, Search SQL/index writes, and
  Vault secret resolution now use authenticated HTTP clients with Data-owned wire records.
  Data-owned controllers dispatch directly instead of calling `IPlaceContextService`.
- Jobs no longer references Application or shared `PlaceContext.Infrastructure`. Jobs owns its
  workload, scheduling, run-status, queue, and test-store contracts; hosted scheduling workers use
  Identity tenant-catalog HTTP and AgentChat launchpad HTTP. Runtime secrets/MCP/OpenSearch
  environment and post-job artifact storage cross authenticated HTTP boundaries.
- Projects no longer directly references Application. Graph hotspot reads and activity-event
  publication now have Projects-owned HTTP seams to Data and Jobs. Its executable still composes
  legacy shared Infrastructure for project persistence; that transitive seam must be removed before
  Application can be deleted.
- Artifacts now owns the internal post-job output ingestion endpoint and Data owns OCR-result
  persistence. Search owns internal SQL/index/environment endpoints; Vault owns internal secret
  resolution; MCP owns internal job-environment token resolution; Identity owns the internal tenant
  catalog endpoint.
- Verified with zero warnings/errors: Data API, Jobs API, Projects API, Search API, Artifacts API,
  Vault API, and the existing AgentChat API. MCP API is green after its warning cleanup and needs one
  final rerun in the closing verification batch.
- The Jobs test project currently does not compile because legacy tests still construct the removed
  in-process CRM/communications/artifact/embedding/Data-recorder dependencies. Migrate those tests to
  caller-local HTTP client fakes before claiming the Jobs suite green again.
- CRM and AgentChat now build independently after their remaining in-process Jobs/Data/Search/
  Artifacts/Projects/Identity calls were replaced with authenticated HTTP clients and caller-local
  wire DTOs. Jobs sends CRM completion callbacks over HTTP.
- Remaining direct Application project references are now exactly shared Infrastructure and legacy
  Host. Identity, Projects, and Agents still reach Application transitively by composing shared
  Infrastructure, so Host and Application remain intentionally undeleted.

### Communications and edge-token checkpoint — 2026-08-09

- `PlaceContext.Communications` now owns the `communication_providers` persistence model, its own
  `CommunicationsDbContext`, `__EFMigrationsHistory_Communications`, safe initial adoption migration,
  database health check, provider validation/resolution, and Postmark, SendGrid, and Twilio HTTP
  integrations. Provider credentials remain Vault-owned and are resolved by authenticated HTTP.
- Communications now owns the browser settings contract at `/api/v1/settings/communications` and
  service-only capabilities/email/SMS endpoints at `/api/communications/internal`. Settings setup
  reads Projects and Vault metadata over authenticated HTTP; no service project-references either.
- The migrated `CommunicationProvidersController` and its Host-only request/response records have
  been removed from `PlaceContext.Host`; the App route catalog sends that browser surface directly
  to Communications.
- CRM and Jobs now send mail/SMS through Communications over authenticated HTTP. Identity two-factor
  is the remaining runtime caller of the legacy shared communication sender and must be converted
  during Identity persistence/composition extraction before the old Postmark implementation can be
  deleted from shared Infrastructure.
- The React/App edge now exchanges the Identity cookie for a five-minute signed service JWT and
  forwards it to non-Identity services. Identity routes alone retain cookie forwarding. App tests
  cover cookie exchange, bearer forwarding, cookie isolation, and direct Identity forwarding (28/28).
- Focused zero-warning builds pass for App, AgentChat API, CRM API, Communications API, Jobs API,
  Projects API, and Vault API. Identity token issuance tests pass 2/2. The architecture suite still
  cannot start because the legacy Host has the pre-existing undefined `_mcpClient` compile error.
- This is not yet a proven Host/Application-free runtime. The final gate remains a Host-free deploy
  with separate agent-shard and job-cluster configuration, green per-service health probes, and a
  real React/App → Jobs create-and-execute flow.

## Remaining coupling to remove

- Shared Infrastructure still references Application, and Projects/Identity/Agents still compose
  portions of shared Infrastructure. Move each required adapter to its owning service or replace it
  with authenticated HTTP before deleting Application.
- The current host still references service implementations directly.
- Jobs, Data, Search, Artifacts, Vault, AgentChat, MCP, Communications, Settings, Operations, and CRM APIs
  do not require shared Infrastructure. Identity, Projects, and Agents still have transitional
  shared-Infrastructure composition to remove.
- The shared database model and migration history still contain tables for the other services and platform capabilities, preventing their independent database evolution.
- Data and Search now resolve Vault-owned secrets over authenticated HTTP, with service configuration
  retained only as the explicit fallback when no per-project override exists.
- API runtimes currently expose the explicitly migrated controller surface; remaining gateway-only commands must move behind their owning service APIs before the gateway can drop implementation references.
- Data's graph uses Data-owned Jobs snapshot records over HTTP. Artifact and semantic run-output graph
  enrichment still needs owner APIs before parity with the legacy in-process graph is restored.
- The extracted service runtimes need integration-event/outbox wiring; Vault, AgentChat, Agents, Artifacts, Search, Data, Jobs, and CRM now own their database migration and health-check paths.
- Contracts retain legacy `PlaceContext.Application.*` namespaces for source compatibility; physical ownership is correct, namespace cleanup remains.

## Next steps

1. Finish Identity ownership of Access/Auth/Membership/OAuth and its users, roles, API tokens,
   invitations, tenant, and domain persistence. Replace its two-factor sender/provider lookup with
   an Identity-owned HTTP client to Communications, then remove Identity's shared Infrastructure
   composition.
2. Finish Projects and Agents persistence/composition ownership and replace any remaining shared
   Infrastructure adapters with service-owned implementations or authenticated HTTP clients.
3. Remove the released `communication_providers` model and Postmark/SendGrid/Twilio implementation
   from shared Infrastructure after Identity is cut over. Connections remain configured through
   Settings; MCP runtime connections/tokens are consumed by Jobs through the MCP HTTP boundary.
4. Inventory and migrate every remaining Host controller by owner: service-owned single-context
   routes go to that service; backup/cluster operations go to Operations; tenant/branding/menu/
   locality and connection setup go to Settings; only genuine multi-service edge composition and
   static compatibility routes may remain in App.
5. Remove shared Infrastructure's Application dependency, then delete `PlaceContext.Application`,
   `PlaceContext.Host`, and retired CoreApi code only when repository-wide reference/route audits are
   empty. Preserve independent `/health` endpoints on every runtime.
6. Repair or remove the stale Host `_mcpClient` code so the architecture/full-suite checks can run,
   update Jobs/CRM/AgentChat test fakes for the new caller-local HTTP seams, and run all service,
   architecture, frontend, and solution verification.
7. Run a Host-free deployment proof with the agent-shard cluster and job cluster configured as
   distinct workloads. Verify Kubernetes health probes, then create and execute a real job through
   React/App → Jobs and observe terminal run state and artifacts without Host or Application loaded.

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
- Route catalog after the Entity Browse change: six routes remained planned — CRM, Project Data, Data Search, Chat, Artifacts, and Observability.
- 2026-08-09 — Completed the Project Data / SQL Studio React slice. Added the versioned `ProjectDataStudioController` for composed tables, OpenSearch indices, and saved queries plus query execution, saved-query commands, table creation, materialization, and row-link lookup. The React page preserves the table/index/query sidebar, tabs, SQL editor, filtering, table/chart results, JSON inspection, linked-record inspection, and creation/materialization dialogs. Entity Browse received its missing focused test and lint fixes, and the five deferred Events/Data migration files were formatted. Frontend format/lint, catalog and focused data tests, the production frontend build, Host build, section authorization tests, and source-organization tests all pass. Five routes remain planned — CRM, Data Search, Chat, Artifacts, and Observability; Data Search is next.
- 2026-08-09 — Completed the Data Search React slice on the Search-owned `/api/v1/projects/{projectId}/opensearch` boundary. The Search service now exposes a composed page model, dashboard create/update/delete, and settings-gated sync alongside its existing constrained fields and search operations. The React page supports index selection and metadata, generated insights, free-text search, aggregation/chart controls, dashboard edit/duplicate/refresh/delete, paging, query-linked records, and record inspection. Frontend format/lint, the catalog and focused data tests, the production frontend build, Host build, all 37 Search tests, and the 13 source-organization tests pass. Four routes remain planned — Chat, Artifacts, Observability, and CRM; Chat is next.
- 2026-08-09 — Completed the Agent Chat React slice. Added the permission-aligned `/api/v1/projects/{projectId}/chat` browser contract over Agent Chat-owned configuration and persisted sessions, including project/session ownership validation, message sending, and settings updates. The React route preserves project status, conversation history and selection, new sessions, starter prompts, pending-response feedback, quick follow-ups, copy actions, the collapsible side panel, and agent settings. Frontend format/lint, focused Chat and catalog tests, the production frontend build, Host build, all 52 section authorization checks, and the 13 source-organization tests pass. Three routes remain planned — Artifacts, Observability, and CRM; Artifacts is next.
- 2026-08-09 — Completed the Artifact Library React slice. Added the versioned `/api/v1/artifacts` browser composition contract for project/recent files, workspace category rules, permission capabilities, bulk deletion, and expiring public-share lifecycle. The React route preserves project/type/search filters, version-stacked files, paging and selection, deep-linked viewers, inline JSON/CSV/image/PDF/text previews, direct downloads, deletion confirmation, and share create/copy/revoke controls. Frontend format/lint, focused Artifacts and catalog tests, the production frontend build, Host build, and the 13 source-organization tests pass. Two routes remain planned — Observability and CRM; Observability is next.
- 2026-08-09 — Completed the Observability React slice. Added the permission-aligned `/api/v1/observability` composition contract for workspace run/chain history, live in-process telemetry, artifact/span-tree detail, and replay. The React route preserves the job, chain, and live-trace lenses; status summaries; deep-linked run and chain drawers; fan-out pipeline navigation; trace waterfalls; shard/reduce artifacts and logs; project links; and permission-gated replay. Frontend format/lint, focused Observability and catalog tests, the production frontend build, Host build, all 52 section authorization checks, and source organization checks pass. The full Host suite remains at its documented baseline of 287 passing and four unrelated legacy Blazor contract failures (`ProjectData`, `DataGraph`, and `JobChains`). CRM is the only planned route remaining.
