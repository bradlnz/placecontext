# Run jobs and keep the results

Jobs provide the compute behind a workflow. Run your own code or a container image, inspect the logs, and retain generated output as artifacts.

## Define a job

Create a job under **Jobs → New job**. Source jobs support Python, Node.js, Go, Ruby, and .NET, with a multi-file editor. Prebuilt container images provide another execution path.

The basic contract is JSON input on standard input, a primary result on standard output, and logs on standard error. Configure input parameters, a timeout, concurrency, retry behavior, and an optional reduce step.

Network access is disabled unless enabled. Secrets are supplied from the project vault at runtime rather than embedded in source code.

## Test with sample input

Use a job's test blocks to run saved expectations in an isolated sandbox. These tests do not use production writes, network access, secrets, or post-job actions. Inspect failed output and update the job before using it in a live workflow.

## Compose larger processes

Add jobs to sequential or parallel workflow stages. Combine them with queries, agent tasks, waits, decisions, and human responses. Each stage records its output for subsequent work.

Schedules, events, and queues can start configured work. Queue delivery supports delay, retry, and expiry; downstream jobs should handle repeated delivery safely.

## Follow a run

Open a run to see shard status, output, logs, retries, and the executed job snapshot. Running jobs can be cancelled with the appropriate permission. Failed workflows can be replayed from a failed step after inspection and correction.

The log viewer supports search and filters over loaded events. Use **Download logs** for the full available log rather than relying on the shortened table preview.

## Store and share artifacts

Generated reports and files live in artifact storage. Authorized users can preview or download them. HTML and SVG previews use a restricted browser sandbox.

Public artifact links are optional, expiring, and revocable. Anyone with a valid link can view that artifact. A finished workflow can also be shared as a read-only demo snapshot with an optional recorded browser replay.

For deployment storage requirements and detailed job contracts, see the [jobs reference](https://github.com/bradlnz/placecontext/blob/main/src/PlaceContext.Host/Wiki/jobs-and-artifacts.md).
