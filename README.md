<p align="center">
  <img src="src/PlaceContext.Host/wwwroot/favicon.svg" width="112" alt="PlaceContext logo">
</p>

<h1 align="center">PlaceContext</h1>

<p align="center">
  <strong>Turn code and containers into durable jobs that run anywhere you own.</strong>
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub release" src="https://img.shields.io/github/v/release/bradlnz/placecontext?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-43d675?style=flat-square"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square">
</p>

PlaceContext is an open-source, self-hosted job engine with a web portal and an MCP endpoint. Run work on
demand, on schedules, or from events; fan it out across your fleet; connect jobs into pipelines; and keep the
resulting data, logs, traces, and artifacts under your control.

## Quick start

```bash
# Install prerequisites, create local k3d, and pull the verified GHCR image
curl -fsSL https://get.placecontext.io/install.sh | bash
# portal http://localhost:7700 · MCP /mcp
```

Requires Linux or macOS with `curl`. On Linux it uses an accessible system Docker daemon or uses
passwordless `sudo` to install and start Docker, then activates Docker-group access for the invoking
user. On macOS it uses Docker/Colima through Homebrew. Local AI is disabled by default and can be
enabled from the portal after installation. Scripted installs can opt in with `--ai`; local AI also
requires Python 3.11+.
PlaceContext uses [k3s](https://k3s.io), a lightweight Kubernetes distribution, to schedule isolated jobs
across the fleet. For the quick start, [k3d](https://k3d.io) runs k3s inside Docker so no existing Kubernetes
cluster is required. The installer pulls the versioned multi-architecture
[PlaceContext image from GHCR](https://github.com/bradlnz/placecontext/pkgs/container/placecontext).

## Local agent subscriptions

Open **Build → Workflow library** in a project. Retail, professional services, and property operations
each have a free checklist and a detailed agent workflow. An administrator can activate or cancel a
simulated subscription, supply the workflow inputs, and inspect the result and recorded token usage.
Subscriptions apply within the tenant; run history stays within its project. Paid implementations remain
on the server and are not created as editable or exportable customer jobs.

Simulation requires `ASPNETCORE_ENVIRONMENT=Development` and
`PlaceContext__AgentWorkflows__SimulateSubscriptions=true` (enabled in the Development settings).
It collects no money. The demonstration rate is **1 demo credit per 1,000 reported model tokens**.
Missing usage is shown as unknown; Jev usage is excluded because its current integration does not report it.
These totals are not invoices. Configured AI providers may still charge for actual inference.

Paid workflows use the first enabled connection/model in **Settings → Models**, draft a plan, then
ask Jev to verify it. Configure the existing `PlaceContext:TypeSafe` settings for verification. Missing
inputs, uncertain verification, or unavailable Jev require human review. These starter workflows deliver
plans; they do not contact clients, order stock, or book contractors. Free checklists use no model.
Cancelling a subscription blocks new paid runs but preserves in-flight work and historical results.
Request IDs make retries idempotent; an intentional rerun needs a fresh ID.

Production payment collection, reconciliation of missing usage, and provider-specific prices must be
implemented before enabling real paid access. Production hosts reject simulated entitlements.

## Connect your AI harness

PlaceContext exposes the same tools at `<workspace-url>/mcp`. These examples connect to the local quick-start
instance; replace `http://localhost:7700` with your workspace URL.

### Codex

```bash
codex mcp add placecontext --url http://localhost:7700/mcp
```

### Claude Code

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

### Google Antigravity

Open **MCP Servers → Manage MCP Servers**, add a custom server named `placecontext`, and set its URL to:

```text
http://localhost:7700/mcp
```

### Hermes Agent

```bash
hermes mcp add placecontext --url http://localhost:7700/mcp --auth oauth
hermes mcp test placecontext
```

The first tool call opens a browser to sign in with OAuth 2.1 + PKCE. Once connected, try:

```text
Onboard this repository into PlaceContext, then show me its recent activity and available jobs.
```

## Why PlaceContext

- **Project databases and entity models** — create project-scoped SQL tables, define linked entities, browse
  records, map job outputs into tables, and explore relationships without mixing tenant data.
- **Containerised compute** — upload code (`python`, `node`, `go`, `ruby`, `dotnet`) or use a container image;
  PlaceContext fans out sandboxed shards across your fleet, collects results and logs, and persists outputs.
- **Worker networking** — manage VPCs, security groups, and job assignments in separate Networking tabs.
  Kubernetes workers use Kube-OVN for private IPv4 allocation and per-run NetworkPolicies for allowed
  CIDRs and TCP/UDP ports. Unsupported workers reject assigned jobs. See the
  [worker networking guide](src/PlaceContext.Host/Wiki/worker-networking.md) for cluster setup and limits.
  The Network flow canvas shows inbound/outbound rules and assigned jobs. Check an IPv4 flow against
  saved rules or run a temporary outbound TCP probe using the selected VPC and security group.
- **Services** — open **Infrastructure → Services** to launch long-running web app or worker images on
  a Docker host. Services survive portal restarts, restart unless stopped, and expose start/stop/restart,
  removal, status, image health, and bounded logs. Each service has its own network and resource limits;
  web ports bind to host localhost. Images must support non-root execution and a read-only root filesystem
  with writable `/tmp`. Environment variables are stored in Docker, visible to host administrators.
  Apps can submit work to the existing authenticated queue API using a reachable portal URL and API token.
  Kubernetes hosting, public ingress/TLS, persistent volumes, and rolling image updates are not yet supported.
- **Workflows and automation** — pass output through ordered stages, fan out parallel work, add wait or condition
  gates, pause for human or external-agent responses, replay from a failed step, and trigger the whole chain
  on demand, on a schedule, or from an event. In the workflow editor, add a Human in the loop or Agent in the
  loop step with validation instructions and a response field. The configured model generates a question
  from those instructions and the preceding output. Configure `PlaceContext:Qemu:Agent` with a model and
  either the `codex` provider or a `chat-completions` endpoint; question generation does not launch a VM.
  Generation errors fail the step without bypassing review. Human steps accept either a written answer
  or Yes/No buttons; Yes/No answers are JSON booleans, including `false`. The run waits for a fresh response
  through the run panel or `resume_job_chain`; subsequent steps receive that field in the accumulated context. Agent steps
  do not launch an agent automatically. These hand-offs are top-level stages and can have condition gates.
- **QEMU agent steps** — give Jev a browser task with a starting URL and a result to verify.
  Our Rust guest agent controls the image's existing Chromium, with no Python or Node/npm runtime dependency.
  The configured vision model proposes coordinate actions; Jev checks live desktop frames for action approval,
  completion and price evidence. No HTML inspection is used. TypeSafe and vision-model credentials stay on the host. VM disks are disposable, guest networking
  is off by default, and cancellation/timeout stop the VM. Runs return page evidence, a decision
  trace, and replay frames. Configure `PlaceContext:TypeSafe` and a prepared image; use
  `PlaceContext:Qemu:Executor=model` for the older general desktop/terminal agent.
  See the [QEMU guide](deploy/qemu/README.md) for setup, supported controls, and realistic task examples.
  Workflows use `/project/{projectId}/workflows` and Observability uses `tab=workflows&workflowRun={runId}`.
  API trigger/replay endpoints use `/api/v1/workflows/{workflowId}` and cancellation uses
  `/api/v1/workflow-runs/{runId}/cancel`. Existing chain URLs remain compatible aliases.
- **Job logs** — workflow runs and individual job runs include a log event viewer with live updates,
  shard/reduce streams, text search, level filters, line wrapping, pause/resume, and log downloads.
  Filters search the newest 2,000 captured events; the table previews the newest 100 matches with up to
  2,000 characters per message. Downloads include all available output and load only on click. Timestamps
  and levels are shown when emitted by the job. Live output uses the existing per-host buffer;
  completed jobs use retained run logs.
- **Log analytics** — Observability → Logs uses Monaco for command pipelines such as
  `filter level = ERROR | stats count() as errors by job | sort errors desc | limit 20`.
  Select and rename fields with `fields` / `project`; filter with `filter` / `where`; aggregate with
  `stats` / `summarize`; then `sort` and `limit` / `take`. Aggregates include count, distinct count,
  sum, average, minimum, and maximum. Group by fields or UTC time buckets such as `bin(5m)`.
  Azure-style substring conditions support `where message contains "timeout"`, `!contains`, and case-sensitive `contains_cs` / `!contains_cs`, including mapped and JSON fields.
  JSON messages expose nested fields under `json`, for example `stats avg(json.durationMs) by job`.
  Commands execute in order, and result columns follow the query. This is a bounded local pipeline
  language, not a complete KQL or CloudWatch implementation. Existing filter expressions still work.
  Queries cover the latest 100 authorized runs, newest 2,000 events per run, and at most 500 displayed
  rows; aggregations use all loaded events before that display cap. Refresh reruns the query snapshot.
  “Map log fields” maps nested JSON properties into timestamp, level, message, and stream before filtering or aggregating. Blank/missing fields retain detected values; timestamp mappings accept ISO dates. Mappings stay in the current browser session while navigating; original JSON fields and saved logs are preserved.
  The active Observability tab is stored in the URL and survives refresh and browser navigation.
- **Run input files** — the job-run dialog toggles between parameter values (or configured payloads)
  and a JSON file up to 1 MiB. The file must contain an object and required parameters; nested JSON
  is preserved. File input overrides the payload only for that run, leaving job defaults unchanged.
- **Queues** — configure a named queue under a project and link it to a workflow chain. Submit a JSON
  object to `POST /api/v1/queues/{queueId}/messages` using a personal API token with `jobs.run` permission.
  The full object becomes the starting context. A durable HTTP 202 receipt includes a tracking ID and
  `Location` URL (read with `jobs.view`). Optional `Idempotency-Key` values deduplicate retries within
  a queue. Payloads are limited to 1 MiB; names to 120 characters and idempotency keys to 128 characters.
  Pausing rejects new intake; accepted messages retain their original chain. Workers across host replicas
  share PostgreSQL and claim disjoint batches atomically, with heartbeat recovery and the existing
  `PlaceContext:Scheduling:MaxConcurrentChains` limit per replica. This distributes messages dynamically;
  it does not provide fixed partition ownership, per-key ordering, or FIFO execution. Tracking receipts
  expire after a configurable 1–365 days from completion (default 30, hourly cleanup). A message TTL
  of 1–604,800 seconds (default 24 hours) expires waiting messages before dispatch; running workflows
  continue. Default delivery delay is configurable from 0 to 604,800 seconds. Override it for one
  submission with an `X-Execute-At` ISO timestamp up to 365 days ahead; workers never claim it before
  that time. Receipts include `availableAt` and `expiresAt`. TTL starts when a message becomes eligible,
  after its delivery or retry delay. Configure 1–10 total attempts and an initial retry delay of 1–86,400 seconds; the delay
  triples after each failure, capped at 24 hours. Failed workflows restart from the beginning with
  the original payload, including failures after human/agent hand-offs. Make side effects idempotent.
  Exhausted messages retain their encrypted payload for manual retry until receipt cleanup; successful
  and expired messages release it. Previous attempt runs remain linked for inspection.
  Settings are captured on acceptance. Deleting a queue removes intake but preserves
  accepted messages and their receipts. Once a receipt expires, its idempotency key can create a new
  message again. Queue configuration and deletion require `chains.manage` permission.
  The queue list supports search, intake filters, and selection. Each queue has Messages, Monitoring,
  Configuration, and Integration tabs that survive refresh. Monitoring summarizes the latest 50 receipts,
  not the total queue depth. Creation, editing, and deletion use side panes, like other app dialogs.
- **Events** — Activity filters the latest 100 workspace events by project, source, and text, with links
  from system job-completion events to run details and logs. Event types define workspace contracts;
  Subscriptions shows the selected project's event triggers, their enabled state, last firing time,
  and a direct link to manage each job's triggers.
- **Analytics and artifacts** — query project data, create charts and reports, and retain HTML, chart, CSV, and
  structured run artifacts in object storage.
- **Lineage and observability** — inspect job-to-table mappings, run and chain history, shard outcomes, logs,
  OpenTelemetry traces, and operational status across the workspace.
- **Governed access** — project and tenant boundaries, role-based permissions, OAuth 2.1 + PKCE, encrypted
  secrets, and sandboxed jobs with network egress disabled by default.
- **MCP and automation integration** — durable project context and MCP tools let AI and automation clients use
  the same governed data, jobs, pipelines, and artifacts as human operators.

## Screenshots

Agent Store and data profiles below show the actual portal and application shell running locally
with sample project data and fixture services.

![Agent Store preparing project context for an enterprise phone agent](docs/images/agent-store-context.png)

![Data profiles showing measured values, source confidence and evidence checks](docs/images/data-profiles.png)

![Smart dashboard with measured signals and Jev decisions](docs/images/smart-dashboard-decisions.png)

![PlaceContext dashboard with populated charts](docs/images/dashboard-charts.png)

![Job code editor](docs/images/job-code-editor.png)

![Job chain with parallel collection and report publishing stages](docs/images/job-chain.png)

![Job chain workflow canvas](docs/images/job-chain-workflow.png)

![SQL Studio with a query and populated results](docs/images/sql-studio.png)

![Mapped job output in a project table](docs/images/mapped-table.png)

![Project data graph](docs/images/data-graph.png)

## Example deployments

The same setup works for homelab automation, project workflows, and local batch processing. For example, a
document chain can receive a file, run OCR, extract structured fields in parallel, store the records, generate a
PDF report, and send a completion message. Each stage receives the previous stage's primary output, while runs,
logs, retries, and artifacts remain visible in one place.

![PlaceContext spanning local and remote Mac, Linux, VM, and cloud workers](src/PlaceContext.Host/wwwroot/images/cluster-layout.svg)

The control plane and workers can run on bare metal, VMs, Macs, Linux servers, cloud hosts, or a mixture of
them. k3s schedules the work; Tailscale can privately connect nodes that are on different networks.

### Mac-led home or studio cluster

- Run the quick-start installer on an always-on Apple Silicon Mac mini.
- Use the generated command in **Cluster → Add node** to join MacBooks or Linux workstations as workers.
- Add another Apple Silicon Mac as an AI shard when you want local model capacity.
- Tailscale lets machines participate from different networks without opening worker ports publicly.

### Single cloud server

Run a small non-HA workspace on an Ubuntu server and keep AI inference disabled until it is configured
from the portal:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

Point DNS and TLS at the portal, then connect local workers from **Cluster → Add node**. This is a good fit for
evaluation or a small team; production HA uses an existing multi-node k3s cluster, external PostgreSQL, and S3.

### Proxmox homelab

- Run the PlaceContext portal on an always-on Linux VM and create separate worker VMs for local jobs.
- Give CPU-heavy, memory-heavy, or GPU-backed workloads their own workers, then let job chains coordinate them.
- Use schedules for backup checks, media processing, document indexing, sensor rollups, or nightly reports.
- Keep the portal private on the LAN, or use [Tailscale](https://tailscale.com/kb/1017/install) to add workers
  from another site.

A single Proxmox host is an excellent homelab but not HA. A production deployment uses three k3s server VMs
spread across independent Proxmox hosts, separate worker VMs, external PostgreSQL, and off-cluster S3.

In every layout, the portal, schedules, events, and connected AI harnesses submit work to the same durable job
queue. See [`deploy/release/README.md`](deploy/release/README.md) for production requirements and shard options.

## Documentation

- [Installation and fleet setup](deploy/release/README.md)
- [OpenSearch integration](src/PlaceContext.Host/Wiki/opensearch-integration.md)
- [SSO and OAuth integration](src/PlaceContext.Host/Wiki/sso-and-oauth.md)
- [Security and sharing](src/PlaceContext.Host/Wiki/security-and-sharing.md)

The Markdown files under `src/PlaceContext.Host/Wiki/` are embedded into the portal and are the
operator-facing documentation shipped with each build.

## Upgrading

- Re-run the installer to download and verify the newest compiled GitHub release, then roll the deployment.
- Pass `--version v1.2.3` to install or retain a specific release.

## License

**Software:** open source under the MIT License — © Bradley Lietz / CTRL SIGNAL SOFTWARE PTY LTD. See [LICENSE](LICENSE).

**Your data and jobs:** owned by you. PlaceContext does not claim ownership of project data,
knowledge, job definitions, runs, or artifacts you create or import. Details are in [LICENSE](LICENSE).

Third-party components used by PlaceContext are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Contributing and security

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow and
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for community expectations. Please report vulnerabilities
privately as described in [SECURITY.md](SECURITY.md).

### Agent Store and data profiles

**Agent Store** starts with a searchable card catalogue grouped by team, with a compact list
option and run history in a separate view. Switching between Grid and List preserves the
selected agent and its context. Choose an outcome to open its context inspector, review the matching project
sources, and select **Use project data**. Manual context is
available when something essential is missing. Discovery reads bounded samples from explicitly
matched project tables; it does not call a model, scan unrelated tables or contact external
systems. **Continue in Build** opens an editable request to configure the agent, without copying
project records into the link or automatically sending the request.

The catalogue covers abandoned calls, phone-number ownership, routing and failover, call-quality
incidents, phone-service cost exceptions, support escalations, stock replenishment, client
onboarding and maintenance triage. Enterprise cases can start with their core dataset; supporting
policies and targets are optional and stay unknown when absent. Local context checks verify
input presence. Standalone AI plan runs retain development-only subscription simulation and
configured provider requirements; they propose actions for review rather than execute them.
See the [Agent Store guide](src/PlaceContext.Host/Wiki/agent-store.md) for source names and scope.

Both workspaces use locally bundled, prefixed daisyUI components for consistent cards, tabs,
controls and status badges in light and dark themes. See the [workspace style guide](tools/workspace-ui/README.md)
for the pinned component build.

Application icons use a local subset of Lucide SVGs with consistent geometry and semantic
names across navigation, actions, status indicators and enterprise phone agents. The
[icon tooling](tools/icons/README.md) pins the upstream source and licenses and checks that
every registered name resolves to bundled geometry.

**Data profiles** is a smart dashboard over up to 12 available chart snapshots. It measures
each signal's minimum, maximum, mean, median and spread and prepares a small evidence-review
step automatically. **Evaluate with Jev** judges those proposals and opens a decision queue
with Review, Watch, Hold, Needs data or Unavailable states. The score, exact question and
measured basis remain traceable to the snapshot and target fingerprint. Optional targets
compare a named statistic against an explicit limit in chart units and can be saved with the
private view. Data-confidence bands remain separate from Jev's support score.

**Overview** and **Needs attention**
show chart panels together; **Explore values** opens a focused chart and its exact displayed
values. Select a panel to inspect confidence, evidence checks and suggested questions alongside
the charts. Save private named
views, choose and reorder source charts, or explore without saving a layout first. Saved views
and questions remain encrypted and scoped to the current tenant, project and user. Decision
results remain local to the open view, invalidate when evidence or targets change, and never
execute external operational actions. Measurements do not infer units or time trends from
category order. Stale or unusable evidence is held for repair before a model request.

Confidence is derived from the available evidence, with a reason beside each band. Current
means generated within 24 hours; aging means within 7 days. These are review defaults, not
source service-level agreements. Chart snapshots do not preserve source row counts, null rates
or verified sampling coverage, so confidence is capped at Medium. Missing or unusable evidence
is Unknown; stale or structurally ambiguous evidence is Low. Reloading retrieves the latest
saved snapshots; regenerate source charts in **Analytics** to obtain new values.

Suggested questions populate an editable draft. Sending to Jev shares only the selected chart
snapshots and returns an estimated probability of a yes/no answer with an evidence fingerprint.
That estimate does not increase the data confidence level. Changed or missing source charts
invalidate the assessment. Set `TYPESAFE_API_KEY` in the project vault to use Jev (host
`PlaceContext:TypeSafe` or environment settings are the fallback); provider usage is separate
from simulated agent credits. See [Charts and analytics](src/PlaceContext.Host/Wiki/charts-and-reports.md).
