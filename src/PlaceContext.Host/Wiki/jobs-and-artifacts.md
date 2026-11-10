# Jobs, workflows, and artifacts

*Run code, connect steps, and keep the output.*

In **Observability → Job history**, running jobs have **Cancel run** controls in the list
and run details. Cancellation requires permission to run jobs and refreshes the recorded status.

## Jobs

A job uses either source code or a container image. Code jobs support Python, Node.js, Go, Ruby,
and .NET.

The basic contract is:

- input arrives as JSON on standard input;
- the primary result is written to standard output;
- logs are written to standard error;
- network access is off unless enabled;
- vault secrets are supplied as environment variables.

Create a job under **Jobs → New job**. Source jobs have a multi-file editor. A job can define
multiple input payloads, a concurrency limit, success and partial exit codes, retries, a timeout,
parameters, and an optional reduce step.

The Jobs screen gives you a quick health summary followed by the job catalogue. Each row shows the
workload type, output type, number of shards, concurrency, and whether an active trigger runs it
automatically. Use the row actions to run, edit, open source code, or delete the job.

## Tests

Open **Tests** from the project menu or select **View tests** on the Jobs screen. A test block runs
job code in an isolated sandbox with example input. It does not use the network, secrets,
post-job actions, or production data writes.

Use **Run all** to check every enabled test block. Green means the saved expectation passed; red
means it failed. Open the failed result to compare the actual output, then edit the block or its
test methods before running it again.

## Runs

Click **Run** from the job card or editor. Runs stay visible while you navigate elsewhere and
can be cancelled while queued or running.

The run detail shows:

- live logs while the job is running;
- each shard's status, exit code, output, and produced files;
- reduce output when configured;
- retry attempts and the executed job snapshot.

Log tables preview the newest 100 matching events, with up to 2,000 characters per message.
Job log filters search the full text of the newest 2,000 loaded events; container log filters search
all loaded events. **Download logs** retrieves the full available log, including text omitted from
the preview. Pipeline output shows the complete saved result in a JSON editor with word wrapping.
Downloads load only when clicked, keeping large log and file contents out of the pane's initial display.

## Workflows

A workflow runs stages in order and passes each stage's primary output to the next stage.

In the workflow canvas you can:

- drag jobs into stages and reorder them;
- use **Add parallel job** to run more jobs together in a stage;
- drag a path to another stage;
- add wait or condition gates;
- ask a human a question with a written answer or Yes/No buttons;
- pause for a response from an external agent;
- run a selected model automatically with project job tools;
- query project tables directly into a workflow context field;
- evaluate evidence with AI verification or a weighted scoring gate;
- run instructions in a prepared QEMU Linux desktop;
- add an email or SMS action from a **+** connector;
- switch to the list editor when preferred.

Condition gates support existence, equality, text, list, empty, and numeric comparisons.
The canvas shows separate **True** and **False** paths. Use **Edit false path** to add
the jobs that run when the condition is false; an empty false path skips the conditional
stage. Both paths rejoin at the next stage. Parallel jobs within a path all run together,
and the stage waits for every job before continuing.

Email and SMS actions use the workspace's configured communications connection. The workflow only
needs the recipient and message details; it does not need to know which delivery service is behind
that connection. If an action is unavailable, ask an administrator to check **Settings →
Communications** and your send permission.

Open a workflow's **Runs** tab to watch the pipeline update in real time. Select a step to see its
live output and logs. Failed workflows can be replayed from a failed step.

Run details use the stacked workflow canvas, with status colours, durations, and errors on
each job. True and false paths rejoin in the same place as the editor, and jobs that did not
run are marked accordingly. Select a job to inspect its recorded output. This view uses the
current workflow layout with recorded run results; when the steps no longer match that
layout, it shows the recorded steps in execution order instead. The same view appears in
Observability and queue message details.

Human and external review steps use the configured question model to turn validation instructions and
preceding output into a question. Without a question model, human steps show the supplied instructions.
Human steps and **External agent** steps pause until a fresh response arrives. A No answer is a valid
boolean response, not a missing answer or automatic cancellation. Configure a condition such as
`eq:approved:true` on every action that depends on the human response field `approved`; false paths
rejoin the workflow after the conditional stage. Configure the question model
through `PlaceContext:Qemu:Agent` (Codex or a chat-completions endpoint); no VM is launched for questions.
Local Development configuration uses the signed-in Codex CLI with `gpt-6-astra`; override these settings to use another provider or model.

For an automatic verification call, select **AI verification** in an agent step. The AI verifier evaluates the
configured instructions directly against the complete preceding output, sent as JSON text. It
does not call a second model to rewrite the instructions. It saves its probability of Yes in the response field's
`noul` property, alongside the question and model version. Use a condition on a later stage,
such as `gte:agent_response.noul:0.9`, to decide whether to proceed. Obtaining a result does not
mean approval. An administrator must configure the AI verification service. Question generation or verification errors fail the step.

### QEMU browser tasks

QEMU execution is enabled by default. Administrators can disable it with
`PlaceContext__Qemu__Enabled=false`. Native hosts need QEMU installed; the packaged runtime image
includes it. Each execution host still needs a prepared image profile and configured models before
a browser task can run. For containers, mount the image and configure its absolute container path.
Software emulation is the default; KVM is optional and requires access to `/dev/kvm`.

Choose an image profile and give the browser assistant a task in **Task prompt**, including the starting HTTP(S)
URL and the outcome to verify. For example, ask it to place a test order, wait for processing,
check shipping and the total, and refresh to verify the saved record. Enable guest networking
when the target site is outside the VM.

The vision model proposes coordinate actions from live desktop frames, and Jev checks the frame before
execution and when assessing completion. The agent does not inspect HTML or enumerate DOM controls.
Use the live human-control console for login and MFA. Automated uploads and new-window workflows
remain unsupported. Never put credentials in the prompt or workflow context.

The workflow waits for completion, failure, cancellation, or timeout, then discards disk changes.
The output field contains page evidence and a decision trace as JSON in `stdout`, errors in
`stderr`, `exitCode`, `agentSteps`, and `activity`. Replay captures the browser along the way.
A completion score is not proof; independently check consequential results in subsequent stages.

An administrator must configure the browser image and AI verification provider. Model calls run on the host;
keys stay outside the VM. The default browser executor is our Rust agent controlling the existing
Chromium through a private pipe, with no Python or Node/npm runtime. Existing general desktop or terminal prompts
require the legacy `PlaceContext:Qemu:Executor=model` setting and a vision/tool model.

### Queue messages

Open **Queues** to accept JSON messages for a workflow. A queue can delay delivery, retry failed
workflows, and expire waiting messages. Open a queue's **Messages** tab and select **Inspect** to
view a message's workflow run and job logs without leaving the queue. Its **Integration** tab
provides the endpoint and an example request. Monitoring summarizes the latest 50 receipts.

### Example: local backup verification

A homelab workflow might:

1. discover the newest backups on a NAS;
2. run checksums and sample restores in parallel on local workers;
3. wait for every path, then use a condition gate to detect failures;
4. reduce the results into an HTML or PDF report; and
5. store the artifact and send an email or SMS summary.

The same pattern works for OCR and document indexing, media processing, sensor aggregation, or
nightly project reports. A failed stage can be fixed and replayed without rerunning successful
earlier work.

## Submit workflows from another service

For service-to-service ingestion, use the MCP JSON-RPC tools instead of keeping `run_job_chain`
open until a long pipeline finishes:

1. Call `submit_job_chain` with the project ID, either a workflow ID or exact `chainName` (for example
   `full-feasibility-report`), JSON input, and a stable `idempotencyKey` such as the source order UUID.
2. Store the returned `trackingId` and `chainRunId`.
3. Poll `get_job_chain_submission` by tracking ID until `terminal` is true.
4. Download any returned artifact URL with the same OAuth bearer token.

The acknowledgement is returned only after the encrypted input is durable in PostgreSQL. Repeating
the submission with the same idempotency key returns the original receipt. Workers claim requests
atomically across replicas and use the preallocated workflow run ID, so a reconnect, retry, pod restart,
or stale worker claim cannot create a second established run.

Both tools are normal calls to the existing `/mcp` Streamable HTTP endpoint. The JSON-RPC method is
`tools/call`; set `params.name` to `submit_job_chain` or `get_job_chain_submission` and authenticate
with an OAuth access token or a scoped personal API token. `Queued`, `Running`, and `Waiting` are non-terminal. `Succeeded`,
`Partial`, `Failed`, and `Cancelled` are terminal.

## Job keep-alive and cancellation

Jobs establish a durable keep-alive before starting work and renew it every 10 seconds.
Losing that connection cancels execution; cancelled or expired runs cannot renew it. The
background sweep marks records with no pulse for 60 seconds as failed on its next 30-second sweep.
Closing the browser does not stop the execution service or its pulse.

Kubernetes jobs also have a renewable 60-second execution deadline, capped by the job's
configured maximum timeout. If the execution service crashes, or cannot establish or renew
the pulse, Kubernetes terminates the containers. Dependency-build jobs use the same protection.
This requires a functioning Kubernetes control plane and reachable worker node; an isolated
node cannot be forcibly stopped until the cluster regains contact. Docker and external native
workers receive cancellation while the host is alive, but do not have this independent
Kubernetes deadline after a host crash.

**Cancel** saves the cancelled status before removing containers, stopping waiting shards and
preventing late completion from replacing that status. Upgrade the portal to apply the
`AddJobRunHeartbeat` migration and apply the updated `placecontext-jobs` Role (Jobs need `patch`
permission for pulse renewal). Existing runs from an older portal retain their original deadline.

Workflow cancellation is saved before child-job cleanup. Running action steps and pending steps
become cancelled, while completed steps and their checkpoint remain available. Database concurrency
checks reject late workflow writes after cancellation, including when a worker finishes during cleanup.

Kubernetes resource-quota admission conflicts and quota-evaluation timeouts retry before creation,
with exponential backoff and jitter, up to five attempts total. Cancellation stops the retry wait.
Already-existing resources, quota exhaustion, and unrelated server errors are not retried by this
policy because they do not establish that a safe, transient admission failure occurred.

## Artifacts

Every job declares a return type: JSON, table, chart, HTML, CSV, text, PDF, image, or video.
PlaceContext stores the resulting file and any optional post-job outputs.

Use **Artifacts** to browse results across the workspace. Repeated output from the same job is
grouped into versions.

Select a file to preview it. Use **Open** when you want the browser's full viewer or need to
download a file type that cannot be previewed in the portal.

On a phone or small tablet, PDF previews show every page in one vertical list. Pages are prepared
as you scroll so a long document does not have to render all at once. **Open** remains available if
you prefer the device's full-screen PDF viewer.

## Share an artifact publicly

People normally need to sign in and have artifact access. When somebody outside the workspace
needs one run artifact:

1. Open the file in **Artifacts** and select **Share**.
2. Choose an expiry of 1, 7, or 30 days.
3. Select **Create public link**, then copy the link. The full link is shown only once.
4. Send it only to the intended recipient.

> Anyone who has the link can open that artifact without signing in. Treat the link like a
> password and do not use it for a file that should remain private.

Open **Share** again to see whether the link is active and when it was last opened. **Rotate link**
creates a new code and immediately invalidates the old one. **Revoke** removes public access. Each
artifact version has its own share link, and deleting an artifact also removes its link.

The Share button is only shown to users with permission to publish artifacts. Read-only viewers do
not receive that permission by default.

### Sign in and help a QEMU workflow

In the QEMU step, enable **Pause for login before starting**. Open the run: the live desktop is
the main view. It stays locked while automation runs; **Take control** pauses the agent and
unlocks keyboard and mouse input for login or MFA. Replay capture pauses while you have control.
Choose **Save login** to keep a private encrypted browser session for future tests, then
**Resume automation** to lock input again. Select that session in the step's settings for subsequent runs.
Use **Take control** whenever the running agent needs help; it waits for a safe pause.
The timeout includes human interaction, and closing the pane does not resume the agent.
Use **Forget selected login** to remove a saved session. Login sessions are private to their
creator in the project; other operators must use their own.

AI verification throttling (HTTP 429 or 529) uses exponential backoff and the provider's
`Retry-After` delay. If the HTTP retries are exhausted, the workflow waits and retries the
same AI step up to three times, after approximately 30, 60, and 120 seconds (longer when
requested by the provider). The checkpoint and retry count survive restarts. Completed
jobs are not rerun, and downstream steps wait for successful verification. Exhausting
these retries fails the workflow without approving its records.

A background reconciliation job runs at startup and every 30 seconds. It scans failed
and abandoned workflows in pages of 100, including historical failures. One malformed
record does not stop the sweep. It also reconciles standalone job receipts and applies
configured queue retry limits and delays, even when every execution slot is busy.
Exhausted retries stay failed; it does not blindly replay completed work.
It waits at least two minutes beyond the job's retry delay and checks active worker
heartbeats, execution leases, and newer job attempts before correcting the workflow.
Waiting retries and human-input holds stay paused. Corrections update the stored steps
and workflow state; the notification watcher then updates the existing notification.
If a continuation claim expires and another worker takes over, cleanup from the old worker
cannot clear the new claim or reset its workflow to waiting.
An abandoned workflow whose child job was cancelled also becomes cancelled, and its queue receipt
is closed without retrying. A failed sibling retains the workflow's failed outcome.
Each reconciliation sweep logs how many records it repaired and queues it retried or closed.

### Agent tasks and repeat flow

**Agent task** assigns requirements or work. **Agent in the loop** adds reasoning or review.
Both default to **Automatic** when created in the editor: choose an enabled model connection and model
from **Settings → Models**. The runner can inspect, author, run, and check project jobs, subject to
permissions. It uses sandboxed jobs for code execution, stores a complete JSON object in the configured
response field, and continues. Its limits are 20 model turns and 30 minutes; recorded activity links to
the jobs it runs. The allowed tools do not expose the host filesystem or workflow, schedule, permission,
or credential management. A returned result is not proof of correctness; inspect actual job evidence.

Choose **External agent** to wait for a connected agent's written response instead. External **Agent task**
instructions are preserved exactly, while an external **Agent in the loop** step adds a generated review
question. **AI verification** is available on Agent in the loop and records a probability rather than
running a general-purpose agent. Human approval remains a separate step.

**Scoring gate** evaluates an evidence field against weighted criteria and minimum confidence. It stores
`passed`, the score and criterion breakdown, blockers, model, evaluation time, and an evidence SHA-256.
It stops the workflow when the criteria fail by default. If you disable **Stop on failure**, branch
explicitly on the saved result, for example `eq:release_review.passed:true`.

Add **Repeat flow** as the final stage to repeat all preceding steps with their accumulated output until
its completion condition passes. It allows 1–10 total attempts (default 3), persists its attempt count
across restarts and input handoffs, and fails when the last attempt does not meet the condition.
Use conditions and evidence-producing jobs to distinguish failed quality assertions from execution errors,
which stop the workflow. Repeated stages run again, including any external side effects.
