# PlaceContext Microservice Migration Progress

Last updated: 2026-08-08 (Australia/Brisbane)

## Target architecture

- Independently replaceable services: AgentChat, Artifacts, CRM, Data, Jobs (jobs, chains, schedules and triggers), Search (including OpenSearch), and Vault.
- Each service owns its contracts, domain, implementation, controllers, runtime, persistence adapters, and tests.
- Shared types live physically under `src/PlaceContext.Application`; low-level shared contracts are compiled by `PlaceContext.BuildingBlocks` to avoid circular dependencies.
- `PlaceContext.Host` is the temporary API gateway/BFF. Its Blazor frontend will later be replaced by a root-level React application in `frontend/`.
- Service APIs will have explicit route ownership such as `/api/jobs`, `/api/search`, `/api/data`, `/api/vault`, `/api/crm`, `/api/agent-chat`, and `/api/artifacts`.

## Completed and verified

- Split production C# source files so each has exactly one logical declaration at most, including nested classes, records, structs, interfaces, enums, and delegates.
- Reorganized role folders so checked folders contain matching types (`Services` → `*Service`, `Handlers` → `*Handler`, etc.).
- Created service implementation and nested contract projects for all seven services.
- Created service-owned domain projects for AgentChat, Artifacts, CRM, Data, Jobs, Search, and Vault.
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
- Added service-owned ASP.NET controllers and stable route prefixes for all seven services; the current gateway discovers them as MVC application parts.
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
- All seven API runtimes pass Development-mode dependency validation; Jobs reached a listening Kestrel endpoint and Vault `/health` returned HTTP 200 `Healthy`.
- Source organization, required-layer, and project-boundary architecture tests: 11/11 passing.
- Service test projects passing independently:
  - AgentChat: 42 tests
  - Artifacts: 56 tests
  - CRM: 19 tests
  - Data: 167 passing, 1 integration test skipped
  - Jobs: 208 passing, 5 Docker integration tests skipped
  - Search: 32 tests
  - Vault: 4 tests
- Full architecture suite: 11/11 passing after the Vault and AgentChat persistence ownership moves.
- Vault tests are 4/4 passing after its database and encryption lifecycle extraction.
- Search tests are 32/32 passing after the OpenSearch infrastructure/test move.
- Data tests are 167/167 passing with one local-Postgres integration test skipped after its infrastructure/test move.
- Jobs tests are 208/208 passing with five Docker integration tests skipped after its infrastructure/test move.
- CRM tests are 19/19 passing after its infrastructure/test move.
- Artifacts tests are 56/56 passing after its infrastructure/test move.
- AgentChat tests are 42/42 passing after its owned database, unit-of-work, and repository-test move.
- Shared Infrastructure tests: 96/96 passing after obsolete Vault implementation coverage moved to Vault and contract-level fakes replaced concrete Vault EF dependencies.
- Complete test set: 870 passed, 6 environment-dependent integration tests skipped, 0 failed.
- Artifacts presigned-URL coverage now allows the AWS SDK's two-second signing-clock tolerance instead of intermittently requiring an exact expiry second.
- React frontend: lint passes with zero warnings, 12 test files/23 tests pass, and a production build succeeds when emitted to an isolated output directory.

## In progress

- Continue splitting the shared `AppDbContext`, row model, and migration history; Vault and AgentChat are complete and the remaining service/platform tables still need owned database lifecycles.
- Decide explicit ownership for the remaining platform capabilities in shared Infrastructure, especially authentication/tenancy, communications, embeddings/vector storage, cluster integration, and analytics refresh.

## Remaining coupling to remove

- Service implementations still reference the main Application assembly as an extraction seam.
- The current host still references service implementations directly.
- Remaining extracted service adapters still depend on the shared `PlaceContext.Infrastructure` assembly for `AppDbContext`, persistence rows, security, and tenancy primitives; Vault and AgentChat no longer do.
- The shared database model and migration history still contain tables for the other services and platform capabilities, preventing their independent database evolution.
- Standalone Data/Search runtimes currently fall back to service configuration when Vault adapters are absent; replace this transition seam with authenticated service-to-service Vault contracts.
- API runtimes currently expose the explicitly migrated controller surface; remaining gateway-only commands must move behind their owning service APIs before the gateway can drop implementation references.
- Search's decision-tree read model consumes Jobs, Data, and Artifacts domain models transitively; replace these with service contracts/read models.
- The remaining service runtimes need owned databases/migrations and integration-event/outbox wiring; Vault and AgentChat now own their database migration and health-check paths.
- Contracts retain legacy `PlaceContext.Application.*` namespaces for source compatibility; physical ownership is correct, namespace cleanup remains.

## Next steps

1. Extract the next bounded persistence slice (Artifacts is the leading candidate) from shared `AppDbContext`, following the proven Vault/AgentChat context, migration-history, and unit-of-work pattern.
2. Expand each service controller to cover its remaining gateway-only commands and replace cross-service repository reads with HTTP/integration-event contracts.
3. Remove direct service implementation references from the gateway in favor of HTTP clients/integration events.
4. Continue the root `frontend/` migration one vertical slice at a time after each corresponding runtime/API boundary is independent.
5. Apply the referenced .NET architecture guidance per microservice while retaining ubiquitous-language aggregate names and filename/type alignment.
6. Continue reducing `PlaceContext.Infrastructure` to shared platform primitives only.
