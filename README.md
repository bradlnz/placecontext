<p align="center">
  <img src="docs/images/placecontext-corgi.svg" width="112" height="112" alt="PlaceContext corgi logo">
</p>

<h1 align="center">PlaceContext</h1>

<p align="center">
  <strong>Chat with your data. Put agents to work.</strong>
</p>

<p align="center">
  Chat, specialist agents, and your data platform in one workspace.
</p>

<p align="center">
  <a href="https://github.com/bradlnz/placecontext/releases/latest"><img alt="GitHub release" src="https://img.shields.io/github/v/release/bradlnz/placecontext?style=flat-square"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-43d675?style=flat-square"></a>
</p>

PlaceContext is an open-source agentic workflow platform you work with through chat. Ask questions
about your data, choose the right agent for the task, and turn a conversation into work you can
run, review, and repeat.

Your agents share a workspace with connected data, jobs, workflows, and saved results. Keep the
conversation open while you inspect a chart, review an output, or answer a human checkpoint.

[Get started](#get-started) · [See the chat](#start-with-a-conversation) ·
[Create your agents](#create-your-agents) · [Work with your data](#work-with-your-data) ·
[Documentation](#documentation)

## Start with a conversation

Describe the outcome you want. Ask the assistant to find evidence, explain a result, prepare a
report, or help run a process. Follow up in the same conversation and inspect the work beside it.

**Example conversation**

> **You**
>
> Review these sample accounts for onboarding: Harbour Studio and Banksia Works are ready;
> Riverbend Labs is missing details. Show me the next steps.

> **Researcher**
>
> Two accounts are ready for onboarding. One needs review.
>
> - **Harbour Studio:** prepare the onboarding brief.
> - **Banksia Works:** prepare the onboarding brief.
> - **Riverbend Labs:** confirm the missing details.
>
> Switch to **Reviewer** to check the evidence, then use **Handoff writer** to prepare the brief.

This example uses supplied sample records. Agent selection stays in the chat header, and you
can inspect work in the side pane while continuing the conversation.

## Create your agents

Create specialists with their own model, instructions, and assigned MCP connections. Choose an
agent in the chat header as the work changes.

| Agent | What you might ask it to do |
| --- | --- |
| **Researcher** | Gather evidence, summarise records, and flag missing information. |
| **Reviewer** | Check recommendations against their sources and explain disagreements. |
| **Handoff writer** | Turn reviewed findings into a clear brief for the next person or team. |

Open **Agent → Instructions → Add agent**, configure the specialist, and select **Save agents**.
Use shared instructions to keep your agents aligned on how you work.

To coordinate specialists in repeatable work, give each automatic workflow step its own task,
model, and output. [Build the three-agent example →](docs/multi-agent-workflows.md)

## Work with your data

Bring databases, lakes, warehouses, and API feeds into the same project as your agents. Ask
questions over connected sources, turn processing results into project tables, and keep the
evidence behind an answer close to the conversation.

- **Connect the context.** Set up sources through secure connection forms and retrieve relevant records when needed.
- **Make the results useful.** Work with project tables, SQL queries, charts, and generated artifacts.
- **Act from chat.** Create and run jobs or workflows, then inspect their status and output in the side pane.
- **Keep people involved.** Pause for input or approval and continue from the recorded response.

[Explore the data platform](docs/platform-reference.md#data-platform) ·
[Project data and queries](src/PlaceContext.Host/Wiki/project-data.md)

## The harness behind your agents

PlaceContext supplies project context, tools, saved memory, and the model/tool loop that lets an
agent do work. Open **Agent** to inspect instructions and available tools; follow tool results,
job activity, and workflow outputs as the work progresses. Access follows your project permissions.

Run code through sandboxed jobs, bring in MCP tools, and use human checkpoints where a decision
needs judgement. Automatic workflow agents have bounded execution and recorded results.

Use the built-in chat, or connect Codex, Claude Code, and other clients through
[MCP](docs/platform-reference.md#connect-your-ai-harness).
[Agent configuration](docs/build-chat.md) · [Execution controls](docs/platform-reference.md#agentic-workflows)

## Make useful work repeatable

Once a process works, keep it as a workflow. Combine agents, data queries, code, conditions, and
human decisions, then run it on demand or configure a schedule. Data queries can appear wherever
the process needs them.

[See the customer-onboarding example](docs/workflow-tour.md) ·
[Agents and human decisions](docs/platform-reference.md#decisions-and-human-approval) ·
[QEMU browser tasks](deploy/qemu/README.md)

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
| Work in chat and configure models | [Dashboard chat](docs/build-chat.md) |
| Create and coordinate multiple agents | [Three-agent walkthrough](docs/multi-agent-workflows.md) |
| Connect and work with data | [Project data, sources, and queries](src/PlaceContext.Host/Wiki/project-data.md) |
| Build and run workflows | [Jobs, workflows, and artifacts](src/PlaceContext.Host/Wiki/jobs-and-artifacts.md) |
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

