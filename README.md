<p align="center">
  <img src="docs/images/placecontext-corgi.svg" width="112" height="112" alt="PlaceContext corgi logo">
</p>

<h1 align="center">PlaceContext</h1>

<p align="center">
  <strong>Put agents to work. Keep people in control.</strong>
</p>

<p align="center">
  An agentic workflow platform for work that needs reasoning, action, and human judgement.
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub release" src="https://img.shields.io/github/v/release/bradlnz/placecontext?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-43d675?style=flat-square"></a>
</p>

PlaceContext is an open-source agentic workflow platform with a built-in agent harness. Create
specialist agents, give each workflow step its own task and model, and connect their results to data,
tools, and human decisions. The harness runs the model and tool loop, carries context between steps,
and records the work so you can inspect and improve it.

Use it to qualify a lead, review a document, prepare a report, or check a software release.
You decide what runs automatically, what needs review, and what happens next.

[Get started](#get-started) · [Use cases](#what-you-can-build) ·
[Multiple agents](#build-a-workflow-with-multiple-agents) ·
[Data](#data-that-drives-the-next-step) · [Decisions](#agents-people-and-decisions) ·
[Documentation](#documentation)

## Watch PlaceContext in action

The onboarding walkthrough follows a sample multi-agent workflow from the dashboard:
querying project data, assigning specialist agents, reviewing recorded activity, requesting
human approval, and presenting the final result.

[**Watch the onboarding walkthrough (MP4)**](docs/videos/placecontext-onboarding.mp4)

> **Demo disclosure:** This recording uses a browser sandbox with simulated agent execution
> and fictional data. It is not evidence of a live model/provider run. For verified
> automatic-agent execution, follow the
> [live recording guide](scripts/demo/onboarding-dashboard-video.md).

## What you can build

These are examples of workflows you can compose with PlaceContext:

| Use case | A workflow you could build | The outcome |
| --- | --- | --- |
| **Lead onboarding** | Gather lead data, check required fields, ask an agent to recommend the next step, and request approval for exceptions. | A reviewed lead ready for follow-up. |
| **Document processing** | Extract records, validate them, route ambiguous results to a person, and save the accepted data. | Structured information ready to use. |
| **Research and reporting** | Collect sources in parallel, analyse the findings, review the evidence, and generate a report. | A repeatable reporting process with inspectable results. |
| **Software delivery** | Assign implementation work, run tests, request an independent agent review, check the browser experience, and ask for sign-off. | A reviewable handoff with test and review evidence. |

## Build a workflow with multiple agents

Give each agent a clear responsibility. A researcher prepares the evidence, a reviewer checks it,
and a handoff writer turns the approved result into the next team's brief. Each automatic step can
use its own model and instructions; its named output becomes context for the following steps.

| Agent | Responsibility | Recorded output |
| --- | --- | --- |
| **Researcher** | Assess the account records and recommend who is ready for onboarding. | `research` |
| **Reviewer** | Check that recommendation against the original records and flag missing evidence. | `review` |
| **Handoff writer** | Prepare the onboarding brief after a person approves it. | `handoff` |

Create named chat agents through **Agent → Instructions → Add agent**, choose their models,
instructions and MCP connections, then **Save agents**. Switch between them in the chat header.
For workflow execution, add **Agent task** or **Agent in the loop** steps, choose **Automatic**,
and configure each step's model, instructions and response field in the workflow editor.
Saved chat profiles and workflow steps are configured separately. **Agent → Sub-agents** shows
the automatic agents in your accessible workflows.

[Build this three-agent example →](docs/multi-agent-workflows.md)

## Data that drives the next step

Connect databases, lakes, warehouses, and API feeds to your project. Query records, run processing
jobs, and turn their outputs into project tables, reports, and artifacts that the next step can use.

Add **Query data** wherever the workflow needs evidence: before an agent task, between steps,
or after a human decision. Keep data, execution, and recorded results in the same project.

[Explore the data platform](docs/platform-reference.md#data-platform) ·
[Project data and queries](src/PlaceContext.Host/Wiki/project-data.md)

## Agents, people, and decisions

An agent can prepare the work. Jev or another LLM can assess it. A person can approve it.
Your workflow decides how those responses affect the next action.

| Concept | What it does | Example |
| --- | --- | --- |
| **Query data** | Selects project records at any workflow stage and passes them to subsequent steps. | Gather evidence for an agent or prepare a handoff after approval. |
| **Agent task** | Gives an agent work to complete. | Review an application and prepare a recommendation. |
| **Agent in the loop** | Adds an AI review or assessment at a checkpoint. | Check whether the recommendation is supported by the evidence. |
| **AI decision** | Assesses recorded workflow data using a Jev System One model or LLM output. | Turn an AI assessment into a condition input while keeping human approval separate. |
| **Human in the loop** | Pauses for a person's answer or approval. | Resolve missing information or approve an exception. |
| **Decisions** | Routes work using recorded results and rules you choose. | Continue when checks pass; request review when they do not. |

You configure the AI source, checks, approval steps, and both decision paths. Jev System One can provide
a structured verification result, while an LLM can provide an output that your workflow records and
evaluates. An AI recommendation is an input to that process; actions that need human approval must
depend on the person's response.
See the [workflow and decision reference](docs/platform-reference.md#decisions-and-human-approval)
for configuration examples.

[See the customer-onboarding example →](docs/workflow-tour.md)

## Work with the platform through chat

Describe the outcome you want, ask the assistant to build or run a workflow, and inspect its
progress and output beside the conversation. Switch named agents as you work, ask follow-up
questions, and respond when a workflow needs human input.

The workflow defines the steps, dependencies, and decisions. Chat is one way to create and
operate it; you can also use the workflow editor, APIs, or connected MCP clients.

[Explore the dashboard and chat](docs/build-chat.md)

## The harness behind the workflow

The harness is the execution layer around your agents: it supplies project context and tools,
runs model calls and tool responses, and saves each step's result for the next agent or decision.

- **Work with context.** Pass named outputs between agents; query data wherever the workflow needs it.
- **Act through tools.** Run code through sandboxed jobs and use MCP tools with project and permission checks.
- **Keep execution bounded.** Automatic workflow agents have a 20-turn, 30-minute limit, with cancellation and recorded failures.
- **Inspect and intervene.** Follow model activity, tool calls, job logs and JSON results; pause for human input and resume the workflow.

Use the built-in dashboard assistant, or connect an external harness such as Codex or Claude Code
through [MCP](docs/platform-reference.md#connect-your-ai-harness).
[Explore agent execution and controls →](docs/platform-reference.md#agentic-workflows)

## Built for work you need to own

- **Data and execution together.** Connect sources, query records, run jobs, and keep their outputs
  in the same project. Use the data you already have to drive the next action.
- **Room for real workloads.** Combine your own code and containers with agents, parallel work,
  quality checks, and browser automation. Run on demand, on a schedule, or when new work arrives.
- **Your infrastructure and model choices.** Self-host PlaceContext and connect your chosen
  model providers. Project permissions, encrypted secrets, and isolated jobs help control access.
- **Fits your existing tools.** Work in the web portal or connect AI clients such as Codex and
  Claude Code through MCP. APIs and Terraform support integration and repeatable setup.

Browser automation uses disposable QEMU desktops, with live human control when a task needs
help or a login. QEMU is enabled by default; a prepared desktop image and configured models
are required. [Set up browser tasks →](deploy/qemu/README.md)

## Get started

Install on Linux or macOS:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

Open **http://localhost:7700**, create a project, and start with one useful workflow:

1. **Connect your context.** Add a data source or import records into a project table.
2. **Build the process.** Describe the outcome to the assistant or compose steps in the workflow editor.
3. **Run and review.** Inspect the results, answer any human checkpoints, and add a schedule when ready.

The installer sets up a local container-based environment. It requires `curl` and Docker access;
installing prerequisites may require administrator permissions. For AI features, add a model
connection under **Settings → Models**.

[Installation guide](deploy/release/README.md) ·
[Connect an AI client](src/PlaceContext.Host/Wiki/mcp-and-jobs.md) ·
[Configure models](docs/build-chat.md)

## Documentation

| I want to… | Start here |
| --- | --- |
| Build and run workflows | [Jobs, workflows, and artifacts](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md) |
| Create and coordinate multiple agents | [Three-agent walkthrough](docs/multi-agent-workflows.md) |
| Connect and work with data | [Project data, sources, and queries](src/PlaceContext.Host/Wiki/project-data.md) |
| Use chat and configure models | [Dashboard chat](docs/build-chat.md) |
| Configure decisions and execution | [Platform reference](docs/platform-reference.md) |
| Check data quality | [Data contracts and readiness checks](docs/job-data-saas-foundation.md) |
| Integrate with other systems | [Management API](docs/management-api.md) · [Terraform](terraform/README.md) |
| Manage access | [Security and sharing](src/PlaceContext.Host/Wiki/security-and-sharing.md) · [SSO](src/PlaceContext.Host/Wiki/sso-and-oauth.md) |

## Open source. Your work stays yours.

PlaceContext is licensed under the [MIT License](LICENSE). You retain ownership of your data,
job definitions, runs, and artifacts. © Bradley Lietz / CTRL SIGNAL SOFTWARE PTY LTD.

Want to help shape it? See [Contributing](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md). Report vulnerabilities through the
[security policy](SECURITY.md). [Third-party notices](THIRD-PARTY-NOTICES.md).

