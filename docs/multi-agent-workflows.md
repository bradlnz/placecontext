# Build a workflow with multiple agents

Create three specialists, pass their results through one workflow, and let a person decide when
the final handoff is prepared. This walkthrough uses sample account records and returns JSON;
it needs a model connection but no external CRM, messages, container jobs, or browser VM.

## Create your agents

1. Add an enabled model connection under **Settings → Models**.
2. On the project dashboard, open **Agent → Instructions → Add agent**. Name the agent
   **Researcher**, choose its connection and model, and give it instructions to assess account
   evidence and identify missing information.
3. Add **Reviewer** and **Handoff writer** in the same way. Give the reviewer instructions to
   challenge recommendations against the source evidence; ask the writer for concise onboarding briefs.
   Assign any MCP connections each chat agent needs, then select **Save agents**.
4. Switch agents in the chat header to work with each specialist. Named chat profiles and shared
   instructions are private to you in the current project; the current limit is eight profiles,
   including the default agent.

These profiles configure dashboard conversations. Workflow agents are configured on each workflow
step with their own instructions, model connection and response field. A step does not inherit a
chat profile's instructions or MCP assignments. For this example, create the same three roles in the
workflow editor using the instructions below. You can use the same model for all three, or select
a different configured model per step.

## Compose the workflow

Create a workflow in **Workflows** and add these stages in order. Choose **Automatic** for all three
agent stages, select an enabled connection and model, and use a different response field for each.

| Stage | Editor step | Configuration |
| --- | --- | --- |
| 1. Account evidence | **Query data** | Output field `evidence`; use the SQL below. |
| 2. Researcher | **Agent task** | Automatic; response field `research`; use the researcher instructions. |
| 3. Reviewer | **Agent in the loop** | Automatic; response field `review`; use the reviewer instructions. |
| 4. Approval | **Human in the loop** | Yes/No; response field `approved`; ask the person to review both results. |
| 5. Handoff writer | **Agent task** | Automatic; response field `handoff`; add condition `eq:approved:true` to this stage. |

This example places the query first to provide evidence for the agents. **Query data** can also
appear between agent steps or after a decision. The three agent stages here execute in order;
each gets the workflow's accumulated context in a fresh model session.

### 1. Supply sample evidence

Use this read-only query and preview the three rows. It requires project data access but does
not create a table or change records.

```sql
SELECT *
FROM (VALUES
    ('demo-001', 'Harbour Studio', 'enterprise', true),
    ('demo-002', 'Banksia Works', 'enterprise', true),
    ('demo-003', 'Riverbend Labs', 'review', false)
) AS accounts(account_id, company, segment, active);
```

With output field `evidence`, subsequent agents receive the records under `evidence.rows`.
For the walkthrough, an account is eligible only when `active` is true and `segment` is
`enterprise`.

### 2. Give the researcher a task

Paste into the first agent's instructions:

```text
You are the Researcher. Use only evidence.rows in the workflow context.
Assess every account. An account is eligible for onboarding only when active
is true and segment is "enterprise". Treat missing fields as needing review.
Do not call tools, contact customers, or change any records in this example.
Return one JSON object with eligible_account_ids (array of strings),
held_account_ids (array of strings), and rationale (array of objects containing
account_id and reason). Include every input account exactly once across the
two ID arrays. Do not wrap the object in markdown or in a "research" property.
```

The harness stores that object at `research`. Later steps still have `evidence`, so the reviewer
can check the source records as well as the researcher's conclusion.

### 3. Add an independent review step

Paste into the second agent's instructions:

```text
You are the Reviewer. Independently check research against evidence.rows.
Verify that every source account appears exactly once across the eligible and
held arrays, no IDs were invented, and eligibility matches active=true and
segment="enterprise". Check that each rationale agrees with the source record.
Do not call tools or change any records in this example.
Return one JSON object with passed (boolean), issues (array of strings),
and summary (string). Set passed=false if evidence is missing or any check fails.
Do not wrap the object in markdown or in a "review" property.
```

This produces a separate recorded assessment at `review`. It is model output for a person to
inspect; a successful model call does not establish that the recommendation is correct.

### 4. Ask a person to decide

Configure **Human in the loop** with **Yes/No**, response field `approved`, and these instructions:

```text
Review evidence, research, and review in the run output.
Should the workflow prepare an onboarding brief for the eligible accounts?
Answer Yes to prepare the brief, or No to hold for follow-up.
```

Yes records `approved: true`; No records `approved: false`. The condition on the next stage
controls execution. No completes this response step; it does not automatically cancel the run.

### 5. Prepare the approved handoff

Configure the last **Agent task** with condition `eq:approved:true` and these instructions:

```text
You are the Handoff writer. Use evidence.rows, research, review, and approved.
Prepare a concise onboarding brief for the eligible account IDs in research.
Include the review summary and any unresolved issues so they remain visible.
Do not call tools, send messages, or change records in this example.
Return one JSON object with account_ids (array of strings), brief (string),
and next_steps (array of strings). Do not wrap it in markdown or a "handoff" property.
```

On Yes, the writer produces `handoff`. On No, the writer is skipped and the run ends without
a handoff. The hold outcome is the recorded No response and skipped writer; it does not send a
follow-up message. Gate any additional actions that require approval in the same way.

## Run and inspect the harness

Save the workflow, use **Pre-run checks**, and start a fresh run. Model calls use your configured
provider. The agent instructions above request no tools for this example; they do not revoke
the runner's available tools or replace permission controls.

- In **Agent → Sub-agents**, inspect each automatic workflow agent's model, task instructions
  and output field, plus the shared workflow-agent system prompt.
- In the run panel, follow each agent's activity and JSON output. When tools are used in other
  workflows, the same panel records tool activity and links to job runs and logs.
- At the approval step, inspect `evidence`, `research` and `review`, then answer Yes or No.
- Use separate fresh runs to check both paths: Yes should produce `handoff`; No should mark the
  writer Skipped with no new `handoff` output. Confirm that `demo-001` and `demo-002` are eligible
  and `demo-003` is held. Inspect disagreements instead of treating the sample outcome as guaranteed.

Each automatic step uses a bounded model/tool loop: at most 20 model turns and 30 minutes,
a complete JSON object as its final result, permission checks, cancellation, and recorded errors.
The workflow preserves the named outputs between stages; duplicate response fields would replace
an earlier result. Code runs through jobs, and the runner has tools for project jobs and MCP calls.
Workflow steps do not automatically inherit a saved chat agent's MCP allowlist.

For external agents, choose **External agent** instead of Automatic and supply the fresh response
at the handoff. Connect Codex, Claude Code or another MCP client using the
[AI harness connection guide](platform-reference.md#connect-your-ai-harness). External-agent
handoffs and automatic workflow agents have different execution paths; the automatic runner's
20-turn and 30-minute bounds apply to Automatic mode.

This is a configuration walkthrough checked against the implementation. Its diagram and example
outputs are illustrative; it does not claim a recorded live multi-agent run.
The existing [README screenshots](images/workflow-screenshots.md) document a separate
single external-agent example.

[Back to the README](../README.md#build-a-workflow-with-multiple-agents) ·
[Agent execution reference](platform-reference.md#agentic-workflows) ·
[Decisions and human approval](platform-reference.md#decisions-and-human-approval)
