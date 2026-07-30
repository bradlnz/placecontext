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
- switch to the list editor when preferred.

Condition gates support existence, equality, text, list, empty, and numeric comparisons. A
parallel stage waits for every path before continuing.

Open a chain's **Runs** tab to watch the pipeline update in real time. Select a step to see its
live output and logs. Failed chains can be replayed from a failed step.

## Artifacts

Every job declares a return type: JSON, table, chart, HTML, CSV, text, PDF, image, or video.
PlaceContext stores the resulting file and any optional post-job outputs.

Use **Artifacts** to browse results across the workspace. Repeated output from the same job is
grouped into versions.
