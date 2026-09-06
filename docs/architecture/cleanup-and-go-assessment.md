# Repository cleanup and Go assessment

**Status:** proposal, not a migration commitment. **Reviewed:** 6 September 2026.
**Baseline:** `f09c283e2637e77c76d14b2027f9bf11a854ad9c` (`main` at review time).

This is a source-level review of the solution, application composition, host startup, cluster HTTP
boundary, selected tests, and release workflow. It is not a complete security audit or a performance
benchmark. The accompanying change updates documentation and branding only; it changes no executable
code, schemas, deployments, authentication, or repository visibility.

## Recommendation

Keep the .NET control plane and Blazor portal. Finish logical module boundaries and introduce a
repeatable performance baseline before selecting any runtime to replace. Evaluate **ClusterHost** as
the first Go experiment because it is already a separate HTTP service without project references to
the application, domain, or infrastructure assemblies. Do not start with a whole-platform rewrite.

A language change alone does not establish a performance improvement. A proxy rewrite also cannot
remove time spent in model inference, Kubernetes scheduling, database queries, or remote storage.
Measure those separately before attributing end-to-end latency to .NET.

## What is already in place

The [solution](../../PlaceContext.slnx) contains five source projects, six test projects, and a shared
test-support project. Preserve this investment rather than creating a new folder hierarchy from scratch.

| Boundary | Current role and evidence |
| --- | --- |
| Domain | Core model and inward dependency boundary; protected by [layer tests](../../tests/PlaceContext.Architecture.Tests/LayerDependencyTests.cs). |
| Application | Commands, queries, ports, orchestration, and feature directories such as Jobs, Projects, DataMappings, Graph, and OpenSearch. [Composition](../../src/PlaceContext.Application/DependencyInjection.cs) still registers many features centrally. |
| Infrastructure | External adapters and persistence. The [host startup](../../src/PlaceContext.Host/Program.cs) registers this layer and persists the Data Protection key ring through `AppDbContext`. |
| Host | ASP.NET Core, interactive Blazor, controllers, MCP, authentication, and telemetry. See [startup](../../src/PlaceContext.Host/Program.cs) and [dependencies](../../src/PlaceContext.Host/PlaceContext.Host.csproj). |
| ClusterHost | Independent local-AI HTTP service with [its own project](../../src/PlaceContext.ClusterHost/PlaceContext.ClusterHost.csproj), [startup](../../src/PlaceContext.ClusterHost/Program.cs), and [controller](../../src/PlaceContext.ClusterHost/Controllers/ClusterProxyController.cs). |
| Operations | [Release packaging](../../.github/workflows/release.yml), installers, k3s manifests, and optional Python local-AI worker under `deploy/release/`. |

The baseline already includes the shared-type split from PR #15. This proposal builds on that cleanup;
it does not recommend repeating it. Feature-oriented directories also already exist. The next step is
clear ownership and dependency rules, not merely moving files.

## Cleanup in priority order

### 1. Put the existing test investment on the pull-request path

At the reviewed revision, `.github/workflows/` contains the release workflow. It is triggered by
version tags or manual dispatch, runs on a restricted self-hosted runner, and executes only the
`ReleaseInstallerContractTests` filter before publishing. That is not a full-solution PR test gate.

Add a separate, non-publishing PR workflow with read-only permissions on isolated hosted runners.
Restore, build, and test `PlaceContext.slnx` with .NET 10; make unit, architecture, and service-dependent
integration checks explicit. Provision disposable PostgreSQL or other services only for suites that
need them. Do not run untrusted PR code on the production/self-hosted release runner or expose release
secrets to it. Retain installer checks as a separate release concern.

**Acceptance:** all six test projects are accounted for, failed tests block the PR check, and PR jobs
have neither publishing credentials nor deployment side effects. The workflow is proposed, not added
by this documentation change.

### 2. Finish feature ownership without multiplying projects

[Application composition](../../src/PlaceContext.Application/DependencyInjection.cs) imports broad
`Features`, `Dtos`, and `Ports` namespaces and registers handlers for many unrelated capabilities in
one method. For example, the file at
[`Jobs/Services/JobTestFramework.cs`](../../src/PlaceContext.Application/Jobs/Services/JobTestFramework.cs)
still declares `PlaceContext.Application.Features`.

Keep the five projects initially. Within Application, give each capability its own registration method,
commands/queries, contracts, and services. Retain a small `AddApplication()` composition root that calls
these registrations. Do not replace explicit registration with unconstrained assembly scanning.

Use a small ownership map to decide cross-feature calls:

| Capability group | Scope | Boundary rule |
| --- | --- | --- |
| Access | Authentication, membership, tenancy, permissions, secrets | One policy decision path; do not duplicate authorization in each transport. |
| Automation | Jobs, chains, triggers, events, run lifecycle | Own run-state transitions, retries, cancellation, and idempotency. |
| Project data | Entities, mappings, links, saved queries, search | Own data contracts and project/tenant scoping. |
| Knowledge | Context, activity, graph, decisions, requirements, skills | Consume explicit application contracts, not another module's persistence internals. |
| Adapters | Database, Kubernetes, object storage, AI and other integrations | Implement application-owned ports in Infrastructure; do not move I/O into Domain. |

These are proposed ownership groups, not new projects or renamed directories. Narrow the broad
`PlaceContextService` facade gradually where a consumer needs only one capability. Preserve public
contracts during that process. Align internal namespaces with ownership in small changes; check
reflection, DI resolution, serialization, and tests before any namespace rename.

**Acceptance:** module registrations resolve every handler exactly once; dependency tests catch
outward references and forbidden cross-module calls; feature behavior remains unchanged.

### 3. Make Host startup a composition root, not a policy implementation file

[`Host/Program.cs`](../../src/PlaceContext.Host/Program.cs) currently configures telemetry, response
compression, rate limits, local-AI configuration logging, view-model lifetimes, Data Protection, and
authentication in one entrypoint. Extract focused registration and pipeline functions into the existing
`Host/Startup/` area. Keep policy implementations in their owning modules.

Do this one concern at a time. Preserve middleware order, cookie/JWT/API-key behavior, tenant scoping,
rate-limit partition keys, Blazor circuit lifetimes, MCP routes, and shared Data Protection keys.
Cookie and vault compatibility are migration constraints, not incidental implementation details.
Correct stale startup comments only after checking the actual authorization pipeline.

**Acceptance:** startup stays readable and behavior tests prove the same authenticated and rejected
requests, tenant boundaries, route contracts, and cross-replica cookie/key behavior.

### 4. Test contracts rather than source spelling

Keep the existing assembly-level [architecture tests](../../tests/PlaceContext.Architecture.Tests/LayerDependencyTests.cs).
Selected host tests, including
[`RemainingShellMvvmTests`](../../tests/PlaceContext.Host.Tests/RemainingShellMvvmTests.cs) and
[`RpcChainIngestionContractTests`](../../tests/PlaceContext.Host.Tests/RpcChainIngestionContractTests.cs),
also assert source-text fragments. Those assertions can make a behavior-preserving extraction fail
without proving that the HTTP behavior works.

Supplement them with DI-resolution tests and black-box HTTP/transport tests. Replace source-string
checks only when equivalent behavior coverage exists. Keep deliberate installer and packaging
source-contract checks where they protect a real distribution contract.

**Acceptance:** moving a registration or method does not require changing unrelated tests, while
changing authorization, request validation, cancellation, or response schemas still fails tests.

## Where Go fits

| Candidate | Recommendation | Reason and migration cost |
| --- | --- | --- |
| ClusterHost | First controlled experiment | Independent project and small HTTP boundary. Must preserve chat, streaming, embeddings, health, authentication, and shard behavior. |
| A future isolated worker or ingestion adapter | Consider after profiling and contract extraction | Potential operational benefit from an independently deployable executable, but the boundary must be real before selecting a language. |
| Job/chain scheduler and persistence | Not the first migration | Run ownership, retries, replay, cancellation, and schema compatibility need explicit contracts and failure testing before introducing a second implementation. |
| Blazor portal, OAuth, tenant policies, Data Protection | Keep in .NET for now | A Go rewrite would also replace or bridge UI/runtime and security dependencies. It is not a mechanical translation of C# files. |
| Optional Python local-AI worker | Keep its existing boundary | Replacing a proxy does not require rewriting the inference worker. |

### A bounded ClusterHost experiment

Only after agreeing the approach in an issue, add an isolated prototype under
`experiments/clusterhost-go/` with its own `go.mod`, `cmd/clusterhost/`, and `internal/` packages.
**That directory and a Go implementation do not exist as part of this change.** Keep the deployed
.NET service and its configuration as the default throughout the experiment.

First capture a shared, language-neutral contract suite from the current controller and
[authentication middleware](../../src/PlaceContext.ClusterHost/Security/ClusterApiAuthenticationMiddleware.cs):

- Cover `/api/cluster/health`, `/chat`, `/chat/stream`, and `/embeddings` under the same route prefix,
  including headers, request/response schemas, validation failures, and rejected credentials.
- Preserve the startup body limit, per-request validation, stream framing and termination, timeouts,
  disconnect cancellation, error behavior, shard failure handling, and bounded resource use.
- Test non-ASCII input limits explicitly: C# `string.Length` and Go byte length are not interchangeable.
  Avoid accidental compatibility changes when translating the controller's character limits.

Use deterministic mock shards for correctness and proxy-overhead tests, then the same real shards
for end-to-end tests. Never shadow mutating job submissions or duplicate expensive inference calls
without explicit limits and approval. A controlled canary must route each request to one implementation.

### Performance decision gate

Profile the .NET baseline with OpenTelemetry and, where appropriate,
[`dotnet-counters`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) and
[`dotnet-trace`](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).
Profile the Go experiment using the [Go diagnostics tools](https://go.dev/doc/diagnostics).
Keep diagnostic endpoints private and keep payloads and secrets out of captured artifacts.

Compare the same hardware, resource limits, concurrency, payload distribution, shard responses, and
network topology. Record warm/cold startup, process RSS, CPU per request, image size, p50/p95/p99 latency,
stream time-to-first-token, throughput at a fixed error budget, and recovery after upstream failure.
Report proxy-only overhead separately from model and network time. Repeat runs and retain the harness,
versions, raw results, and variability; there are no benchmark results in this review.

Agree a material-win threshold before testing; for example, at least 25% lower process RSS at equivalent
throughput with no more than a 5% p95 latency regression. These are **proposed decision thresholds, not
measured improvements**. Require complete contract parity, acceptable operational complexity, a bounded
soak test, and a rehearsed rollback as well. If the experiment does not meet the agreed gate, retain .NET.

## Suggested delivery sequence

| Change | Exit condition |
| --- | --- |
| Branding, README, and this assessment | Documentation/asset review; no runtime or deployment changes. |
| PR verification and baseline | Existing test projects run reliably; performance harness is reproducible. |
| Focused composition cleanup | Application registrations and Host startup are modular with behavior parity. |
| ClusterHost Go experiment | Shared contract suite passes; benchmark report justifies or rejects a canary. |
| Optional canary | One routing/configuration change restores the .NET service; no dual schema ownership. |

Keep database migrations owned by the existing .NET system during the experiment. Do not give a Go
prototype write access to application tables or introduce a second migration owner. A broader migration
would need a separate decision covering schemas, durable queue semantics, security, and operator support.
