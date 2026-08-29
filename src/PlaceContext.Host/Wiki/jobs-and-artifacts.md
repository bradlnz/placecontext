# Jobs, chains, and artifacts

*Run code, connect steps, and keep the output.*

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

## Chains

A chain runs stages in order and passes each stage's primary output to the next stage.

In the chain canvas you can:

- drag jobs into stages and reorder them;
- add parallel paths with the **+** control;
- drag a path to another stage;
- add wait or condition gates;
- add an email or SMS action from a **+** connector;
- switch to the list editor when preferred.

Condition gates support existence, equality, text, list, empty, and numeric comparisons. A
parallel stage waits for every path before continuing.

Email and SMS actions use the workspace's configured communications connection. The chain only
needs the recipient and message details; it does not need to know which delivery service is behind
that connection. If an action is unavailable, ask an administrator to check **Settings →
Communications** and your send permission.

Open a chain's **Runs** tab to watch the pipeline update in real time. Select a step to see its
live output and logs. Failed chains can be replayed from a failed step.

### Example: local backup verification

A homelab chain might:

1. discover the newest backups on a NAS;
2. run checksums and sample restores in parallel on local workers;
3. wait for every path, then use a condition gate to detect failures;
4. reduce the results into an HTML or PDF report; and
5. store the artifact and send an email or SMS summary.

The same pattern works for OCR and document indexing, media processing, sensor aggregation, or
nightly project reports. A failed stage can be fixed and replayed without rerunning successful
earlier work.

## Submit chains from another service

For service-to-service ingestion, use the MCP JSON-RPC tools instead of keeping `run_job_chain`
open until a long pipeline finishes:

1. Call `submit_job_chain` with the project ID, either a chain ID or exact `chainName` (for example
   `full-feasibility-report`), JSON input, and a stable `idempotencyKey` such as the source order UUID.
2. Store the returned `trackingId` and `chainRunId`.
3. Poll `get_job_chain_submission` by tracking ID until `terminal` is true.
4. Download any returned artifact URL with the same OAuth bearer token.

The acknowledgement is returned only after the encrypted input is durable in PostgreSQL. Repeating
the submission with the same idempotency key returns the original receipt. Workers claim requests
atomically across replicas and use the preallocated chain-run ID, so a reconnect, retry, pod restart,
or stale worker claim cannot create a second established run.

Both tools are normal calls to the existing `/mcp` Streamable HTTP endpoint. The JSON-RPC method is
`tools/call`; set `params.name` to `submit_job_chain` or `get_job_chain_submission` and authenticate
with an OAuth access token or a scoped personal API token. `Queued`, `Running`, and `Waiting` are non-terminal. `Succeeded`,
`Partial`, `Failed`, and `Cancelled` are terminal.

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
