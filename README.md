<p align="center">
  <img src="src/PlaceContext.Host/wwwroot/favicon.svg" width="112" alt="PlaceContext logo">
</p>

<h1 align="center">PlaceContext</h1>

<p align="center">
  <strong>Turn code and containers into durable jobs that run anywhere you own.</strong>
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub release" src="https://img.shields.io/github/v/release/bradlnz/placecontext?style=flat-square"></a>
  <a href="https://github.com/bradlnz/placecontext/actions/workflows/release.yml"><img alt="Release build" src="https://img.shields.io/github/actions/workflow/status/bradlnz/placecontext/release.yml?style=flat-square&label=release"></a>
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

Requires Linux or macOS with `curl`; the installer provisions the remaining local runtime dependencies.
PlaceContext uses [k3s](https://k3s.io), a lightweight Kubernetes distribution, to schedule isolated jobs
across the fleet. For the quick start, [k3d](https://k3d.io) runs k3s inside Docker so no existing Kubernetes
cluster is required. The installer pulls the versioned multi-architecture
[PlaceContext image from GHCR](https://github.com/bradlnz/placecontext/pkgs/container/placecontext).

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
- **Job chains and automation** — pass output through ordered stages, fan out parallel work, add wait or condition
  gates, replay from a failed step, and trigger the whole chain on demand, on a schedule, or from an event.
- **Analytics and artifacts** — query project data, create charts and reports, and retain HTML, chart, CSV, and
  structured run artifacts in object storage.
- **Lineage and observability** — inspect job-to-table mappings, run and chain history, shard outcomes, logs,
  OpenTelemetry traces, and operational status across the workspace.
- **Governed access** — project and tenant boundaries, role-based permissions, OAuth 2.1 + PKCE, encrypted
  secrets, and sandboxed jobs with network egress disabled by default.
- **MCP and automation integration** — durable project context and MCP tools let AI and automation clients use
  the same governed data, jobs, pipelines, and artifacts as human operators.

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

Run a small non-HA workspace on an Ubuntu server and keep AI inference elsewhere:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash -s -- --no-ai
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

## Developing

```bash
./setup.sh                          # install source-development prerequisites
./run.sh                            # database, build, migrations, app
./run.sh --fresh                    # destructive: recreate the local database, then start
./start.sh                          # later runs: build and start the prepared app
./start.sh --no-build --port 7710   # fast restart on a different port
dotnet build && dotnet test        # all suites green; architecture tests enforce the onion
dotnet run --project src/PlaceContext.Host   # portal http://localhost:7700, MCP at /mcp
```

On a database without a human owner account, opening the portal redirects to the first-run setup.
That flow creates the default workspace owner and signs them in; no shared default password is used.

You'll need the .NET 10 SDK and PostgreSQL (the release cluster provides one, or:
`docker run -d -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=placecontext -p 5433:5432 postgres:16`).
EF migrations apply automatically on startup.

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
