# Host controller migration

`PlaceContext.Host` and the legacy `PlaceContext.Infrastructure` project are removal targets.
The React application calls `PlaceContext.App`; App proxies a request to one owning microservice
or composes read models from several microservices. App must not reference Host, the legacy
Infrastructure project, or a microservice implementation assembly.

## Current status

- Moved to CRM: `CrmIngestionSettingsController` (route unchanged).
- Moved to Search: `OpenSearchProxyController`, now `OpenSearchController` (route unchanged).
- Moved to Artifacts: authenticated run downloads, public share downloads, and chat attachment
  downloads (routes unchanged).
- Moved to Jobs: external event ingestion; all event contracts and handlers now live under Jobs.
- Moved to Jobs: execution orchestration, run recording/watching, test-framework support, embedded
  artifact parsing, and post-job artifact generation previously under `Application/Jobs`.
- `PlaceContext.Application/Jobs` is now empty. Pure job mapping/source-resolution helpers live in
  Jobs Contracts so the transitional Backup feature can consume them without referencing the Jobs
  implementation assembly.
- Moved to Jobs: job-run cancellation, chain-run cancellation, chain trigger, and chain replay;
  service runtime API-key authentication preserves the existing machine-client credential.
- Moved to Jobs: management API read, update, and delete for individual job definitions; the Jobs
  service now owns the public job request/response contracts and mapping used by transitional Host
  endpoints as well.
- Moved to Jobs: management API read, update, and delete for individual schedules; project-scoped
  schedule list/create remain in Host until a Projects service contract is available.
- API-key requests routed through App resolve tenant context from the original, sanitized
  `X-Forwarded-Host`. The resolver is a narrow transitional Infrastructure adapter and must become
  an Identity/Tenants service client before legacy Infrastructure is removed.
- Remaining in Host: 47 controllers.
- Legacy Infrastructure is still referenced by the CRM and Jobs executable projects. Those two
  references are migration debt; they must be replaced by service-owned adapters before the legacy
  project can be deleted.

Host continues to discover microservice controller assemblies during the transition. This keeps
old deployments working, but the controller source and dependency ownership now live with the
microservice. PlaceContext.App routes migrated public paths directly to that service.

## Destination inventory

### Existing microservices

- Agent Chat: `Api/AgentStreamController`, `SlackController`.
- Agents/cluster: `Api/AgentController`, `ClusterPageController`.
- Artifacts: `OcrController` (the completion write must use a Data service contract).
- CRM: `CrmArtifactsController`, `CrmIngestionController`, `CustomerPortalController`,
  `CustomerPortalArtifactsController`, `CommunicationProvidersController`.
- Data: `Api/EntitiesController`, `ConnectionsSettingsController`.
- Jobs: the project-scoped list/create actions remaining in `Api/JobsController` and
  `Api/SchedulesController`, plus `CoreApiJobsController` and `JobMcpController`.
- Search: `Api/SearchController` (blocked on personal-token authentication and project resolution,
  not on the Search query handler itself).
- Vault: `ProjectSecretsController`.

These controllers move only after their handlers, DTOs, authentication contract, and persistence
adapters are owned by the destination service. Cross-service calls use service contracts/clients;
they must not be replaced with a reference to legacy Infrastructure.

### PlaceContext.App composition endpoints

The following browser/page read models combine multiple service responses and therefore belong in
App: `DashboardController`, `WorkspaceController`, `ProjectPageController`,
`ProjectDataGraphController`, `EntityBrowsePageController`, `EventsPageController`,
`JobChainsPageController`, `JobCodePageController`, `JobsPageController`,
`JobTestCodePageController`, `JobTestsPageController`, `SchedulePageController`,
`ProjectAnalyticsController`, `ProjectDataAdminController`, and `InspectorController`.

App composition is read-oriented. Commands continue to go directly to the owning microservice.

### Bounded service still required

- Identity/access: `AccessSettingsController`, `Api/ApiTokensController`, `AuthController`,
  `IdentityController`, `Api/ProjectsController`, `CoreApiProjectsController`,
  `Api/SettingsController`.
- Backup/operations: `BackupController`, `BackupSettingsController`.
- MCP: `McpOAuthController`, `McpSettingsController`.

Until these boundaries exist, the endpoints stay in Host. They must not be moved into App with
their databases or legacy adapters.

### Retire or place directly in App

- `HealthController`: retire when Host is removed; App and every microservice already expose health.
- `CoreApiHealthController`: expose from App if the compatibility route is still required.
- `WikiController`: move its static read model to App while the route remains in use.

## Removal gates

1. React routes no longer render through Blazor/Host.
2. Every Host controller is moved, replaced by an App composition endpoint, or retired.
3. App has proxy coverage for every retained browser/API route.
4. CRM and Jobs no longer reference `PlaceContext.Infrastructure`; no service executable does.
5. Authentication is handled at App/service boundaries without Host cookie or API-key handlers.
6. Host and legacy Infrastructure can be removed from the solution with architecture and service
   tests still passing.
