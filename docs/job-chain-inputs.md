# Pausing a chain for input

A normal chain stage checks its jobs' declared required parameters before
starting any job in that stage. Missing values pause the run as `AwaitingInput`;
later stages stay pending. Zero and false are values, while null and blank text
are missing. Parameters may use dotted paths, such as
`funding_inputs.client_cash`, to address nested envelope fields.

The current envelope, stage index, parameter overrides and input request are
stored in the database. The input request is encrypted with the chain-run
purpose. There is no scheduled wake-up until the requested values are submitted.
The funding job must declare its fields; the runner does not infer required
inputs from arbitrary job output or copy its saved example payload.

`get_job_chain_submission` includes `inputRequest` while the run is paused.
Submit that request through the run page or the `resume_job_chain` MCP tool:

```json
{
  "chainRunId": "<existing run UUID>",
  "inputRequestId": "<inputRequest.id>",
  "valuesJson": "{\"12\":{\"funding_inputs.client_cash\":800000}}"
}
```

The outer key in `valuesJson` is the requested step index. The inner keys are
declared parameter names, including their dotted paths. Existing fields are
preserved; unknown steps/fields and incomplete required inputs are rejected.
A database row lock serializes submissions. Repeating the same request ID and
values returns the current status; conflicting values are rejected. Submission
requires `jobs.run` permission in the run's tenant.

A successful submission schedules the existing durable continuation worker.
It resumes the same run at the paused stage, without rerunning earlier stages.
The funding calculator still validates ranges and financial constraints.

## Parent agent approvals

Choose a parent in **Agents → Overview**. Agents without an explicit parent report
to the master agent. Shared agents can select another shared agent; private agents
can select another private agent. Cycles and unavailable parents are rejected,
and children must be reassigned before their parent can be deleted.

In dashboard chat and automatic agent tasks, the parent reviews the task, each
proposed tool batch, and the final result. Reviews use the parent's system prompt
and configured model (or the running model when unset), without tools. Approval
never expands the child's permissions. These reviews add model calls and latency.
Task and tool authorization gates check whether the proposed scope may proceed;
they do not require completed findings that cannot exist before the work runs.
Result approval reviews the completed evidence and rejects unsupported work.

Automatic **Agent in the loop** steps ask the selected agent's parent directly;
with no selected child, the master agent reviews the gate. The response field
contains `{ "approved": true, "reason": "..." }` on success. Rejection, invalid
approval output, unavailable parents, or model failures stop execution. Previously
completed actions are not rolled back. Human handoffs and external/AI verification
providers keep their existing behavior.

## Jev verification in an agent step

In **Agent in the loop**, choose **Jev (TypeSafe)** under **Responding agent**.
Existing steps default to **External agent** and continue to pause for a submitted
response. The selection is preserved when editing, exporting, and importing workflows.

Configure the question-generating model through `PlaceContext:Qemu:Agent`, then set
`PlaceContext__TypeSafe__ApiKey` or `TYPESAFE_API_KEY` on the application server.
Keep the key in deployment secrets rather than a workflow definition. The default
model is pinned to `jev-1.14.0`; `PlaceContext__TypeSafe__Model` can select another
model available to the account. The key is shared by Jev steps on that server.

The model generates one yes/no verification question from the instructions and
preceding output. The runner sends that question and output to
`https://api.typesafe.ai/v1/systemone`, using a `noul` question. Jev's result is
saved under the configured response field, for example:

```json
{
  "agent_response": {
    "type": "noul",
    "noul": 0.95,
    "model": "jev-1.14.0",
    "question": "Does the reported total match the line items?"
  }
}
```

`noul` is the probability of Yes, not a boolean or a separate confidence score.
A completed verification step means the result was obtained; it does not mean
the output was approved. Set a condition on subsequent stages, such as
`gte:agent_response.noul:0.9`, and choose an appropriate alternate path for other
results. The threshold is a workflow decision, not an integration default.

The request uses the preceding workflow output as remote model input. It is
limited to 65,536 characters and rejected rather than truncated. API errors,
missing credentials, invalid probabilities, and timeouts fail the step before
later stages run. The HTTP call has a 90-second deadline and does not retry
automatically; retry a failed step after correcting the cause. Cancellation is
passed to the HTTP request. No database migration is needed for this selection.

API contract: [TypeSafe System One](https://docs.typesafe.ai/api/system-one),
[Noul](https://docs.typesafe.ai/primitives/noul), and
[model versions](https://docs.typesafe.ai/models).

Deploy the `AddChainInputRequest` migration before starting the updated host.
To run the database regression against an isolated PostgreSQL database:

```sh
PLACECONTEXT_TEST_POSTGRES='Host=localhost;Port=5433;Database=placecontext_test;Username=postgres;Password=postgres' \
  dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~ChainInputResumeTests
```
