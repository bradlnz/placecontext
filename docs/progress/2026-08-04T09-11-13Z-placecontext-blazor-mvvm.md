# PlaceContext Blazor MVVM migration

**Completed:** 2026-08-04 09:11 UTC
**Scope:** `PlaceContext.Host` Blazor pages, layouts, shared stateful components, ViewModels, architecture contracts, README, and license.

## Delivered

- Migrated every stateful Razor page to a dedicated ViewModel.
- Migrated stateful layouts and shared components, including the main/settings shells, route selection, notifications, graphs, chain canvases/pipelines, and parameter inputs.
- Kept Razor limited to markup, component parameters, event forwarding, and Blazor lifecycle attach/detach glue.
- Moved mutable state, service access, validation, commands, navigation, JS interop, formatting, status interpretation, and UI-mode decisions into ViewModels.
- Added typed UI catalogs/enums for data sections, chat/CRM/OpenSearch presentation state, code-editor modes, run/step statuses, and other formerly stringly-typed decisions.
- Registered page ViewModels as circuit-scoped services and repeated component ViewModels as transient services through `IComponentViewModel`, preventing state leakage between repeated component instances.
- Added repository-wide architecture contracts that enumerate all Razor pages and fail on service injection, non-lifecycle code, inline formatting helpers, or raw string state branching.
- Updated README architecture guidance and changed the project license to MIT while retaining the data-and-jobs ownership clarification.

## Isolation

The migration commit was assembled and verified from its staged Git tree rather than the broader dirty working tree. Unrelated customer-portal, job-chain context, infrastructure, CSS, and deployment changes remain unstaged. Customer-portal controls that depend on those unrelated backend changes were intentionally excluded from the staged Access/CRM MVVM snapshot.

## Verification

Verified from a detached worktree created from the exact staged Git tree:

- `dotnet build PlaceContext.slnx --no-restore --no-incremental` — passed.
- `PlaceContext.Host.Tests` via `dotnet vstest` — **265 passed, 0 failed**.
- Strict page MVVM contracts cover every Razor page.
- Shell/shared MVVM contracts and transient component-lifetime contracts passed.
- `git diff --cached --check` — passed.

The solution build retains pre-existing analyzer/package-version warnings, including EF Core relational 10.0.4/10.0.9 resolution and Blazor parameter-forwarding warnings; there were no build errors.

## Deployment

Commit, push, production rollout, remote build-log inspection, and browser smoke validation follow this note and will be reported separately with production evidence.
