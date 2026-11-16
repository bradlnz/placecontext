<p align="center">
  <img src="docs/images/placecontext-brand.svg" width="640" alt="PlaceContext — paired bracket logo and wordmark">
</p>

<h1 align="center">Critical work. Connected by context.</h1>

<p align="center">
  An open-source data and agentic workflow platform for critical workflows.<br>
  Persistent data. Stateless functions. Agents and people in control.
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub releases" src="https://img.shields.io/badge/releases-GitHub-404040?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-404040?style=flat-square"></a>
</p>

PlaceContext connects website inputs, queues and schedules to workflows that coordinate code,
agents, reviews and human decisions. Every workflow returns data or artifacts: records to query,
reports to open, and evidence to inspect. Self-host the MIT-licensed platform on your infrastructure,
with paid support available for deployment, workflow design and operations.

[Get started](#get-started) · [Use cases](#what-you-can-build) ·
[Inputs & schedules](#website-inputs-queues-and-schedules) · [Artifacts](#data-and-artifacts-you-can-use) ·
[Observability](#observability) · [Documentation](#documentation) · [Support](#open-source-and-paid-support)

## Inside the platform

![Website Enquiry workflow with validation, triage, AI review, conditional actions and parallel handoffs](docs/images/enquiry-workflow.png)

**Website input → queue → validate → triage → AI review → Determine → assign → verify → output.**

The Website Enquiry example routes a question to the right person. Stateless code validates the
input, agents query team data and review the recommendation, and conditional steps control the
browser assignment. The workflow checks the saved result before preparing parallel handoffs.

The screenshot shows the local platform with fictional Northstar enquiry data, captured on
16 November 2026. The definition has changed since the recorded successful run on 15 November;
that earlier run does not validate later edits.

[Workflow reference](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md#workflows) ·
[Run the landing page and animated walkthroughs locally](#website-and-documentation-preview)

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
**Determine** asks a Yes/No question about the evidence and chooses a path. Conditions evaluate
recorded results; human checkpoints pause for a person's answer or approval.

Configure both paths and gate protected actions explicitly. Retries handle execution failures
within configured limits; they do not replace validation or human approval.

[Reviews, Determine and approvals](docs/platform-reference.md#decisions-and-human-approval) ·
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

Industry examples are illustrative patterns. The Website Enquiry walkthrough uses the existing
fictional demo project. Full use-case pages and step-through animations are available in the
[local product website](#website-and-documentation-preview).

## Get started

Install on Linux or macOS:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

Open **http://localhost:7700**, create a project, and start with one useful workflow:

1. **Connect your context.** Add a data source or import records into a project table.
2. **Build the process.** Describe the outcome to the assistant or compose steps in the workflow editor.
3. **Run and review.** Inspect returned data and artifacts, answer human checkpoints, and enable a queue or schedule when ready.

The installer sets up a local container-based environment. It requires `curl` and Docker access;
installing prerequisites may require administrator permissions. For AI features, add a model
connection under **Settings → Models**.

[Installation guide](deploy/release/README.md) ·
[Connect an AI client](src/PlaceContext.Host/Wiki/mcp-and-jobs.md) ·
[Configure models](docs/build-chat.md) · [Set up browser automation](deploy/qemu/README.md)

## Website and documentation preview

The landing page, animated use cases and styled documentation live in the separate
`~/code/placecontext-website` repository. To run its local preview:

```bash
cd ~/code/placecontext-website
npm ci
npm start
```

Open **http://localhost:4182**. Website changes belong in that repository; the platform remains here.

## Documentation

| I want to… | Start here |
| --- | --- |
| Preview the landing page, use cases and styled docs | [Website development](#website-and-documentation-preview) — local preview at `http://localhost:4182` |
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
