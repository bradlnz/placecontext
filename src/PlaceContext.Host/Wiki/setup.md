# Setup and settings

*Install PlaceContext, configure a durable instance, and verify it before adding projects.*

## Install locally

Python 3.11+, curl, and OpenSSL are required. The release installer never invokes `sudo`; it uses an
accessible Docker daemon or verified rootless Docker, downloads its own `k3d` and `kubectl`, creates
the local cluster and secrets, starts local AI, and deploys the portal:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

PostgreSQL is installed for the local lab cluster; object storage is optional. The portal is
available at `http://localhost:7700`.

PlaceContext uses [k3s](https://k3s.io), a lightweight Kubernetes distribution, to schedule jobs
across its workers. The local installer uses [k3d](https://k3d.io) to run k3s inside Docker, so a
separate Kubernetes installation is not required. Production connects the same manifests to an
existing multi-node k3s cluster instead.

For a source checkout, install the .NET 10 SDK, Docker, and PostgreSQL, then run:

```bash
dotnet run --project src/PlaceContext.Host
```

## Homelab and Proxmox

For a simple homelab, run PlaceContext on one always-on Linux VM and use **Cluster → Add node** to
join separate worker VMs. Jobs then run on whichever worker has capacity, while the portal keeps
their inputs, logs, retries, and artifacts together.

A practical Proxmox layout is:

- one VM for the PlaceContext portal and local lab cluster;
- separate worker VMs for CPU-heavy, memory-heavy, or GPU-backed local jobs;
- optional Apple Silicon machines joined as AI shards; and
- [Tailscale](https://tailscale.com/kb/1017/install) when a worker or operator is outside the home LAN.

This works well for backup verification, media processing, document indexing, sensor rollups, and
scheduled reports. Job workflows can coordinate those tasks across machines and pass each stage's
output to the next stage.

A single Proxmox host is not HA. Production uses three k3s server VMs spread across independent
Proxmox hosts, separate worker VMs, external PostgreSQL, off-cluster S3, TLS, and tested backups.
See [Cluster and nodes](/wiki/cluster-and-nodes) for local and Tailscale topology diagrams.

## First boot

1. Open `http://localhost:7700`.
2. If there is no owner account, complete the first-run setup form. There is no default password.
3. Follow the cluster setup guide: choose cloud or local execution, connect workers, and check **Settings → Cluster**.
4. For a managed workspace, choose a plan under **Settings → Billing & usage**. Local workers do not remove the managed workspace subscription requirement.
5. Create or onboard a project, then run a small job and confirm its worker, logs and artifact are retained.
6. Export a configuration backup from **Settings → Backup** and store it outside the cluster.

## Production URL and reverse proxy

Use HTTPS before enabling browser integrations or exposing the service outside a private network.
Forward normal proxy headers and WebSocket/streaming traffic to port `7700`, then set the canonical
origin with no trailing slash:

```text
PlaceContext__PublicBaseUrl=https://placecontext.io
```

The canonical URL is used for OAuth issuer metadata, callbacks, MCP challenges, and generated links.
It must match the URL users and external applications actually open. Keep the OAuth signing key and
Data Protection key stable across restarts and identical across replicas; changing either key can
invalidate tokens or protected cookies.

## Configuration and secrets

PlaceContext uses standard .NET configuration. A setting such as
`PlaceContext:OpenSearch:Endpoint` becomes the environment variable
`PlaceContext__OpenSearch__Endpoint`. Environment variables override `appsettings.json`.

Do not commit a populated `.env`, Kubernetes Secret, private key, password, or project Vault export.
Let the release installer create deployment secrets and use an environment-specific overlay for
optional settings instead of editing the shared manifest with real values.

Use **Settings → Connections** for a project-specific database or OpenSearch endpoint. Those
credentials are encrypted in that project's Vault. Use workspace environment settings only when all
projects should share the same service.

## Cluster job admission

Cluster jobs wait for resource pressure to clear before admission. Confirmed resource failures before
user code starts can retry after other jobs in the tenant namespace finish. Application errors and
failures after execution starts are not automatically retried, to avoid repeating external effects.
`PlaceContext:WorkloadRunner:ClusterQueueTimeoutSeconds` bounds admission/startup waiting (default
1800 seconds); `ClusterStartupRetries` limits additional startup attempts (default 3). Execution
timeouts start when the workload container starts, separately from the queue deadline. Scheduler
messages appear in live output and retried startup failures are retained in the final diagnostics.

## Data access audit collection

The host logger category `PlaceContext.Host.Auth.DataAccessAuditMiddleware` emits Information-level
event `7100` (`DataAccess`) for table-list and query requests to `/api/v1/projects/{projectId}/data`
and `/api/v1/job-data`. Events include the resolved tenant, authenticated user or run identifier,
authentication scheme, action, project/job/run identifiers where available, HTTP status, outcome,
duration, a generated audit ID, and the current trace ID when tracing is active. Rejected credentials
have no authenticated actor; job scope is recorded only after successful job authentication.

Events exclude request/response bodies, SQL, credentials, email addresses, and exception details.
They wrap authentication and authorization, so denied requests are included; disconnected requests
are marked `cancelled` with status `499`. These are server-side outcomes, not proof that a client
received every response byte. Requests rejected before tenant resolution/routing are not covered.

Production must collect host logs into an access-controlled destination with an agreed retention
period, restricted deletion, and delivery monitoring. Keep this category enabled at Information or
lower. The default console provider alone does not establish durable or tamper-resistant evidence;
log collection is not a fail-closed authorization control. Confirm collection with a successful read,
a denied read, and an invalid query, then retain evidence of those checks. Broader audit coverage
(including portal, entity APIs, permission changes, and exports) remains in the security backlog.

## Storage and backups

The vector adapters encrypt stored source text (and content source keys) using the existing
Data Protection key ring. SQL-to-vector migration preserves that protection. Existing remote
plaintext records require reindexing or a separate migration; this change does not scan or rewrite
an existing vector service. Vectors remain searchable and must still be treated as sensitive data.
An embedding provider receives input text in order to compute vectors, so encrypting stored text
does not prevent PII disclosure to that provider. PII classification, permitted processing locations,
masking, and authorization to decrypt need explicit policies before ingesting sensitive sources.

Production uses external HA PostgreSQL and off-cluster S3. A settings backup does not include run
history, object-store files, database
contents, or Vault plaintext. A recoverable deployment therefore needs all of the following:

- PostgreSQL backups;
- S3 versioning, lifecycle, and replication/backups;
- the configuration export;
- independently protected deployment secrets and encryption keys.

Test restore procedures on a separate instance. A backup that has never been restored is not yet a
verified recovery plan.

## Optional integrations

- Follow [OpenSearch integration](/wiki/opensearch-integration) to connect searchable indices,
  enable SQL and aggregations, and optionally configure a collector trigger.
- Follow [SSO and OAuth](/wiki/sso-and-oauth) to use PlaceContext sign-in in another platform or to
  sign users into PlaceContext through a compatible identity authority.
- Follow [Cluster and nodes](/wiki/cluster-and-nodes) before adding worker machines.

## Workspace settings

- **Cluster** shows node health and worker join instructions. Use **Cloud or local setup** to reopen the setup guide.
- **Billing & usage** manages the workspace subscription and reports top-level starts. Open the secure billing portal for payment methods, billing details, invoices and subscription changes. Self-hosted infrastructure is billed by your own hosting providers.
- **Branding** changes the workspace name, logo, accent, and dark-mode colours.
- **Menu** controls navigation labels, order, and visibility.
- **Artifacts** controls the file categories shown on the Artifacts page.
- **Communications** connects email and SMS delivery for workflow actions. Jobs and users see
  the generic Email or SMS channel rather than needing to choose a delivery provider.
- **PlaceContext database** is available to owners and super admins from the database selector in **Data → Records**. It uses SQL Studio’s table list, editor, query tabs, and results grid. Application queries support single-table SELECT, COUNT(*), comparisons joined by AND, ORDER BY, LIMIT (up to 500), and OFFSET. Records remain workspace-scoped and sensitive fields are masked; deployment-wide tables expose their schema only. Writes, joins, and other SQL functions are unavailable for application tables.
- **Connections** configures project-specific PostgreSQL and OpenSearch services.
- **MCP servers** connects extra tools that jobs may use.
- **Locality** sets the timezone used by schedules and displayed dates.
- **Backup** exports or imports workspace configuration. It can also download all job source
  files as a ZIP arranged by project and job.
- **Access** manages members, roles, and permission overrides.
- **Security** manages sign-in security.
- **API tokens** creates personal tokens for the entity data and project search APIs.

Backup exports do not include run history or vault secrets. The job-code ZIP also excludes
environment values.

Most settings pages are available only to the default workspace administrator. **API tokens** is
self-service, so signed-in users can manage their own tokens.

## Verification checklist

Before opening the instance to other users, verify:

- the Cluster page reports the expected nodes as ready;
- the public HTTPS URL produces the expected OAuth issuer metadata at
  `/.well-known/oauth-authorization-server`;
- a test job can write logs and an artifact;
- a non-admin account sees only its permitted projects and actions;
- backups exist outside the machine or cluster; and
- credentials are supplied by the deployment environment or project Vault, not tracked files.

## Your display theme

The light/dark switch is in the user area at the bottom of the main menu. It is a personal browser
preference, so changing it does not alter workspace branding or another person's screen. If you use
more than one browser or device, choose the mode separately on each one.
