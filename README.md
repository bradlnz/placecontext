<p align="center">
  <img src="docs/images/placecontext-brand.svg" width="640" alt="PlaceContext — paired bracket logo and wordmark">
</p>

<h1 align="center">PlaceContext</h1>

<p align="center">
  Open-source workflow orchestration, project data and agent execution.<br>
  .NET 10 · Blazor · PostgreSQL · Kubernetes · S3-compatible storage
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub releases" src="https://img.shields.io/badge/releases-GitHub-404040?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-404040?style=flat-square"></a>
</p>

PlaceContext is a self-hosted platform for workflows that combine stateless jobs, containers,
AI agents, conditional gates and human input. Workflows accept JSON from API calls, queues or
schedules and persist step status, logs, structured outputs and generated artifacts.

The web host exposes a Blazor UI, a management API and an MCP endpoint. PostgreSQL stores platform
state and project data; Kubernetes executes isolated job workloads; S3-compatible storage holds
artifacts when configured. AI features use configured external model providers.

[Get started](#get-started) · [Architecture](#architecture) ·
[Workflow execution](#workflow-execution) · [Documentation](#documentation)

## Get started

Choose the packaged installation for local evaluation, or clone the repository for development.

### Install with Bash

Run the same installer documented in the [deployment guide](deploy/release/README.md#locallab):

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

The installer verifies a release bundle and runs PlaceContext in a local k3d/Kubernetes cluster
inside Docker. It supports Linux and macOS; Docker access is required, and provisioning Docker
on Linux may require passwordless `sudo`. This is a single-host evaluation installation.
Object storage is optional and must be configured separately.

### Clone and run from source

Prerequisites: Git, Bash, `curl`, a running Docker daemon, and the .NET SDK selected by
[`global.json`](global.json) (10.0.403, with patch roll-forward).

```bash
git clone https://github.com/bradlnz/placecontext.git
cd placecontext
dotnet restore PlaceContext.slnx
./run.sh
```

`run.sh` creates or starts PostgreSQL 17 on `127.0.0.1:5433`, waits for a database query to succeed,
then builds and runs the web host. Database files persist in the `placecontext-dev-postgres`
Docker volume. Stopping the host leaves the database running. If the app is already healthy,
the script exits without restarting it.

Both paths serve the portal at **http://localhost:7700**. The MCP endpoint is `/mcp` and the
management API is under `/api/v1`. Configure model providers in **Settings → Models**.
Running the source host does not provision a Kubernetes worker cluster, an object store or a
browser VM image; configure those capabilities before running workflows that require them.

For production, use an existing Kubernetes cluster with external PostgreSQL, HTTPS object
storage, TLS and a digest-pinned image. Follow the [production deployment guide](deploy/release/README.md#production-on-kubernetes).
For source development and checks, see [Contributing](CONTRIBUTING.md#development-setup).

## Architecture

| Component | Responsibility |
| --- | --- |
| `src/PlaceContext.Domain` | Entities, value objects, business rules and repository contracts. |
| `src/PlaceContext.Application` | Commands, queries, workflow orchestration and infrastructure interfaces. |
| `src/PlaceContext.Infrastructure` | EF Core persistence, queues, model clients, workload runners and storage adapters. |
| `src/PlaceContext.Host` | ASP.NET Core host, Blazor UI, management API, MCP and authentication endpoints. |
| `src/PlaceContext.DesktopAgent` | Guest agent for browser/desktop automation. |
| `tests` | Layer-specific tests, browser checks and deployment checks. |
| `deploy` | Release installers, Kubernetes resources and VM setup. |

Dependencies point inward: Application depends on Domain; Infrastructure implements their
contracts; Host composes the runtime. Tenant and project authorization applies at application
and persistence boundaries. See [repository conventions](CONTRIBUTING.md#design-boundaries).

## Workflow execution

- Compose sequential stages, parallel branches and sub-workflows in the visual editor or through APIs.
- Run stateless jobs with explicit inputs, runtime configuration, timeouts and recorded outputs.
- Use conditions and AI Review gates to select paths; human input steps pause for a response.
- Trigger runs from API requests, queues or schedules. Queue receipts link intake to execution history.
- Inspect per-step status, logs, retries, structured output and artifacts from the run views.

Persistent state belongs in project data and artifact storage. Jobs must not depend on memory or
files from a previous invocation. Delivery retries can repeat external writes; make those writes
idempotent where required.

## Jev reviews

Learn more on the [PlaceContext website](https://placecontext.io).

Connect **Jev** to evaluate workflow evidence against explicit questions and scoring criteria.
Use the recorded results for deterministic review rules: a Review gate selects a path from a
confidence threshold, and scoring gates apply configured weights and minimum scores. The rules
are deterministic; the underlying model assessment is probabilistic and does not guarantee correctness.
Jev also checks proposed browser actions against live desktop frames.

Add `TYPESAFE_API_KEY` to the project's vault, or configure it on the server before starting the host:

```bash
export TYPESAFE_API_KEY='your-typesafe-api-key'
./run.sh
```

The project vault value takes precedence over server configuration. Configure the service's request
and token limits for your account as described in the [platform reference](docs/platform-reference.md#decisions-and-human-approval).
Jev-dependent steps require this key; ordinary conditions and human input steps do not.

## Inside the platform

![Website Enquiry workflow with validation, triage, AI review, conditional actions and parallel handoffs](docs/images/enquiry-workflow.png)

**Website input → queue → validate → triage → AI review → Review gate → assign → verify → output.**

The Website Enquiry example routes a question to the right person. Stateless code validates the
input, agents query team data and review the recommendation, and conditional steps control the
browser assignment. The workflow checks the saved result before preparing parallel handoffs.

The screenshot shows the local platform with fictional Northstar enquiry data, captured on
16 November 2026. The definition has changed since the recorded successful run on 15 November;
that earlier run does not validate later edits.

[Workflow reference](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md#workflows)

## Data that drives the next step

Connect sources, query project tables with SQL, and pass named results between workflow steps.
Clean and validate output, map accepted fields into tables, and turn the stored records into
analytics and charts.

Jobs are **stateless, Lambda-like functions**. Each invocation takes explicit input, runs focused
code or a container in isolation, and returns a recorded result. Persistent state belongs in project
data and artifact storage, rather than a previous invocation's memory or working files.

[Data platform, mappings and analytics](src/PlaceContext.Host/Wiki/project-data.md) ·
[Stateless jobs](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md)

## Build a workflow with multiple agents

Give each agent a focused responsibility: triage, research, review or handoff. The built-in harness
supplies project context and tools, runs model and tool calls, and records outputs for subsequent
steps. Choose the models and instructions each task needs.

Create and operate workflows through chat, the visual editor, APIs or connected MCP clients.
Use sequential stages, parallel work and sub-workflows to compose a larger process.

[Three-agent walkthrough](docs/multi-agent-workflows.md) · [Agents and chat](docs/build-chat.md)

## Agents, people, and decisions

AI review can gather SQL evidence and run validation code before recording an assessment.
**Review** asks a Yes/No question about the evidence and chooses a path. Conditions evaluate
recorded results; human checkpoints pause for a person's answer or approval.

Configure both paths and gate protected actions explicitly. Retries handle execution failures
within configured limits; they do not replace validation or human approval.

[Reviews and approvals](docs/platform-reference.md#decisions-and-human-approval) ·
[Workflow execution reference](docs/platform-reference.md#decisions-and-human-approval)

## Website inputs, queues and schedules

- **Website inputs:** send form submissions through your backend to a workflow queue. The JSON payload becomes the workflow's starting context.
- **Queues:** configure delivery delay, expiry and retry limits. Inspect message receipts and their linked workflow runs.
- **Schedules:** run the complete workflow at a selected frequency or cron expression, using the workspace time zone and stored inputs.

Keep integration credentials on your server. A receipt confirms intake; the linked run establishes
the outcome. Repeated delivery is possible, so external writes must handle repeated input safely.

[Configure inputs, queues and schedules](src/PlaceContext.Host/Wiki/events-and-schedules.md)

## Data and artifacts you can use

Return structured data for the next step and retain generated files in **Artifacts**. Browse
reports, PDFs, CSVs, charts, HTML, images and video; preview or download the result. Repeated output
from the same job is grouped into versions. Optional public sharing uses expiring, revocable links.

[Jobs and artifact storage](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md#artifacts) ·
[Artifact sharing and access](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md#share-an-artifact-publicly)

## Observability

Follow workflow and job history, compare status and duration, and open a run to inspect its steps,
logs and output. Trace an incoming queue message through execution to the data or artifact it
produced. Live traces show recent execution on the current server replica; recorded run history
provides durable evidence.

[Explore observability](docs/platform-reference.md#execution-and-operations)

## What you can build

| Use case | Example workflow | Returned data or artifacts |
| --- | --- | --- |
| **Website enquiries** | Validate a request, triage it, review the routing and verify assignment. | Routing data and a verified handoff. |
| **Retail product data** | Normalize fields, validate records and map accepted products into tables. | Clean product records for SQL analytics. |
| **Research and reporting** | Gather sources, check evidence and prepare a report. | Structured findings and a report. |
| **Software delivery** | Run checks, review changes and prepare a handoff for sign-off. | Test results and review evidence. |
| **Logistics exceptions** | Classify a shipment exception, review evidence and prepare the next action. | Shipment exception data and a handoff PDF. |
| **Insurance intake** | Validate intake data, assess completeness and route for human review. | Intake records and an evidence PDF. |

Industry examples are illustrative patterns. The Website Enquiry walkthrough uses fictional demo data.

## Documentation

| I want to… | Start here |
| --- | --- |
| Build and run workflows | [Jobs, workflows, and artifacts](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md) |
| Coordinate multiple agents | [Three-agent walkthrough](docs/multi-agent-workflows.md) |
| Connect sources, clean output and build analytics | [Data platform](src/PlaceContext.Host/Wiki/project-data.md) |
| Trigger workflows | [Website inputs, queues and schedules](src/PlaceContext.Host/Wiki/events-and-schedules.md) |
| Inspect execution | [Observability](docs/platform-reference.md#execution-and-operations) |
| Use chat and configure models | [Dashboard chat](docs/build-chat.md) |
| Configure decisions and execution | [Platform reference](docs/platform-reference.md) |
| Check data quality | [Data contracts and readiness checks](docs/job-data-saas-foundation.md) |
| Integrate with other systems | [Management API](docs/management-api.md) · [Terraform](terraform/README.md) |
| Manage access | [Security and sharing](src/PlaceContext.Host/Wiki/security-and-sharing.md) · [SSO](src/PlaceContext.Host/Wiki/sso-and-oauth.md) |

## Open source and paid support

PlaceContext is licensed under the [MIT License](LICENSE). Run it on your infrastructure with
your chosen models. Paid support covers deployment, workflow design and operational help, scoped
to your team. [Discuss support](https://github.com/bradlnz/placecontext/issues).

You retain ownership of your data, job definitions, runs and artifacts.
© Bradley Lietz / CTRL SIGNAL SOFTWARE PTY LTD.

Want to contribute? See [Contributing](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).
Report vulnerabilities through the [security policy](SECURITY.md).
[Third-party notices](THIRD-PARTY-NOTICES.md).
