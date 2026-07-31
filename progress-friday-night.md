# Friday night checkpoint — 31 July 2026

## Where things stand

PlaceContext is on `main` in `/home/brad/code/devcontext`.

The current checkpoint adds project-data address links into OpenSearch and merges OpenSearch into
the existing application-wide search. It has been built and tested and is ready to deploy.

Production infrastructure already in place:

- Self-hosted OpenSearch runs on the DigitalOcean droplet at `137.184.69.136`.
- OpenSearch is reached privately through Tailscale; credentials remain in PlaceContext Vault.
- The cluster currently has 17 application indexes and approximately 1.98 million documents.
- The PlaceContext Kubernetes deployment is on `170.64.208.233`.
- Do not put API keys, Vault values, Tailscale auth keys, or Postmark tokens in this repository.

## Completed in this checkpoint

### Application search → OpenSearch

- The global search in `MainLayout.razor` now supplies the active project id.
- `SearchHandler` performs one bounded OpenSearch request for that project (eight hits maximum).
- OpenSearch is queried only when the caller has `data.read`.
- Failures/missing OpenSearch configuration remain best-effort and do not break ordinary workspace
  search.
- Result titles prefer address, official/display name, name, and title fields; subtitles add useful
  locality/council/status/type context.
- Results deep-link to Data Search with the source index, original query, and document id.

### Project-data addresses → OpenSearch

- Added reusable `OpenSearchDataValue.razor`.
- Explicit address/street fields and street-number-shaped address values render as links.
- Lone suburb/state/postcode fields intentionally remain plain text to avoid noisy false-positive
  links.
- Links are present in:
  - SQL query result tables;
  - the full-screen table query modal;
  - entity record tables;
  - entity record detail panels.
- Project Data now enforces the existing `data.read` policy at the route.

### Data Search deep links

- `OpenSearchData.razor` accepts `index`, `q`, and `document` query parameters.
- A deep link selects the requested concrete index, loads fields, runs the query automatically, and
  marks the target document row.
- Existing generated charts still load for ordinary non-query navigation.

## Validation completed

- `dotnet build src/PlaceContext.Host/PlaceContext.Host.csproj --no-restore`
  - passed with 0 warnings and 0 errors.
- `dotnet test tests/PlaceContext.Application.Tests/PlaceContext.Application.Tests.csproj --no-restore`
  - 400 passed, 0 failed.
- `dotnet test tests/PlaceContext.Host.Tests/PlaceContext.Host.Tests.csproj --no-restore`
  - 62 passed, 0 failed.
  - Existing EF Core package-version warnings remain in the host test project.

## Previous production checkpoint

Before tonight's changes:

- Last deployed PlaceContext commit: `76a03a96231555758dee7344342c1ac4c93c3139`
- Last deployed image digest:
  `sha256:3a5e9d5f17f1bb82fa9fa1c103765b0d75d576c678ac371936295ee720619537`
- Last known healthy pod: `placecontext-6848976f4d-mcwzv` (2/2 ready)
- Property/OpenSearch repository: `/home/brad/code/property_intelligence_local_opensearch`
- Property repository last known commit: `6c8ec92`

Update this section after tonight's deployment with the new commit, image digest, and pod.

## Tomorrow: remaining work

### 1. Finish section-level RBAC

The permission system and per-user Allow/Revoke/Inherit matrix already exist. Complete route/API
alignment so hiding a menu entry also blocks direct navigation.

Recommended changes:

- Add `crm.view` as a separate permission. CRM currently uses `data.read`.
- Keep `artifacts.view` separate so CRM and Artifacts can be granted/revoked independently.
- Change CRM menu/page/controller to `crm.view`.
- Align generic `[Authorize]` routes:
  - Artifacts → `artifacts.view`
  - Chat → `agents.chat`
  - Data Entities / Data Map / Analytics → `data.read`
  - Events → `events.manage` (or introduce `events.view` if read-only access is needed)
  - Job Chains → `chains.manage` or split view/manage deliberately
  - Jobs → `jobs.view`
  - Job Editor → `jobs.edit`
  - Schedules → `triggers.manage`
  - project overview/dashboard/onboarding → `projects.view`
- Update `MenuConfigService`, `RolePermissionDefaults`, access-permission tests, and controller
  policies together.
- Suggested default: Viewer does not get CRM; Member/Admin/Owner do. Artifacts can retain the current
  Viewer default because a user-specific revoke is already supported.

The authorization audit output from tonight showed generic authorization still on About, API
Tokens, Artifacts, Chat, Dashboard, Data Entities, Data Map, Events, Inspector, Job Chains, Job
Editor, Jobs, Onboarding, Overview, Project Analytics, Project View, Schedules, Security Settings,
and Wiki. Not all need a section permission, but every sensitive section does.

### 2. Add a native Postmark “Send email” chain action

Existing reusable pieces:

- Postmark settings and Vault-backed token resolution:
  `PostmarkConnectionService`
- Sender:
  `IClientCommunicationSender.SendEmailAsync`
- Concrete implementation:
  `SendGridTwilioCommunicationSender` (uses Postmark when connected)
- Editor:
  `JobChainsViewModel.Editor.cs`, `JobChains.razor`, `ChainCanvas.razor`
- Execution:
  `RunJobChainHandler`
- Persistence:
  `JobChain`, `ChainStage`, `JobChainRow.StagesJson`,
  `EfJobChainRepository`

Do not embed a Postmark token in a job or chain. Use the existing tenant-level Postmark connection,
whose token references an encrypted project Vault secret.

Production-ready design requirements:

- Represent Send Email as a typed chain action visible on the canvas/list, not a magic job id.
- Configuration: recipient, recipient name, subject, text body; support payload-path/template
  substitution from the previous stage output.
- Validate recipient/subject/body at save time.
- Execute through `IClientCommunicationSender` with cancellation support.
- Record provider/message id and failure status in chain-run history.
- A Postmark rejection must fail the action/stage and stop later stages.
- Add a dedicated permission such as `email.send` (or deliberately reuse a non-CRM communication
  permission); enforce it in UI and server execution.
- Preserve backward compatibility with legacy `StagesJson` arrays and backup manifests.
- Add domain serialization, handler, authorization, and UI tests.

### 3. Resolve full-feasibility release blockers without weakening accuracy gates

Job source repository:

`/home/brad/code/ossen-reports/placecontext_jobs`

The report generator is not crashing. It deliberately produces a degraded/remediation report
because the site-accuracy checklist has five unresolved client-release blockers. Latest inspected
run:

- Checklist run id: `163785af-7bc5-4dfd-a1a5-a90856032628`
- Recorded: `2026-07-31 11:07:50Z`
- Summary: 27 total, 19 pass, 5 review, 2 fail, 1 n/a, 5 blockers
- `report_allowed = true`, but financial/client/email/planning release remains false.

The five blockers:

1. `services.length_classification` — separate measured water/sewer lengths and Urban Utilities
   classification are missing.
2. `services.stormwater_lpd` — lawful point of discharge is not verified.
3. `siting.design_controls` — measured setbacks, site cover, height/storeys from natural ground,
   parking schedule, driveway gradients/transitions, and retaining/cut-fill are incomplete.
4. `finance.valuation_release` — valuation release does not match evidence sufficiency.
5. `finance.acquisition_price` — no verified subject acquisition price is available.

Next investigation:

- Inspect those five check definitions in `site-accuracy-checklist/main.py`.
- Inspect the latest plaintext job artifact in
  `proj_6525de7dbe5d427dba8746b7154e430c.job_run_data`.
- Trace the exact accepted input paths for verified acquisition price, services, and measured siting
  controls.
- Correct valuation-release consistency if the checklist is treating an intentionally withheld
  valuation as a blocker.
- Do not lower thresholds or silently mark unavailable evidence as verified.
- Keep the generated degraded report, but block the new email action from client delivery unless
  the release object explicitly permits email/client release.
- Run `test_site_accuracy_checklist.py`, deploy with `deploy_p0_jobs.py`, and verify a production
  run.

### 4. Add tests for tonight's OpenSearch search merge

Existing search tests still pass because the new dependencies are optional. Add focused tests with
fake `IOpenSearchDataGateway` and `IPermissionService` for:

- address/name result mapping;
- exactly one bounded query for the active project;
- no query without `data.read`;
- graceful OpenSearch failure;
- deep-link URL encoding.

Also consider debouncing/cancelling stale global-search requests in `MainLayout.razor` if rapid
typing produces overlapping calls. The server already caps each call at eight hits.

## Restart order

1. Pull/check `main` and read this file.
2. Confirm tonight's deployment pod and smoke-test global search with a known address.
3. Add focused OpenSearch search tests.
4. Finish RBAC and its tests.
5. Implement the typed Postmark chain action and its tests.
6. Diagnose/fix the feasibility evidence inputs/checklist consistency.
7. Build, run targeted/full tests, deploy, smoke-test permissions and Postmark in production.
8. Commit/push both `devcontext` and `placecontext_jobs`; update this document with final commits.
