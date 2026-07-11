# Jobs and artifacts

*Write a little code that reads a JSON input and prints a JSON result — PlaceContext runs it safely and saves that result as the job's artifact.*

## The idea

A **job** is some code you write. When it runs, PlaceContext feeds it an input, runs it in a
safe, isolated sandbox, and captures whatever it prints as the job's **artifact** — the result
the job exists to produce. A job that hasn't produced an artifact yet hasn't done anything yet.

The rule of thumb: **print JSON**. If your result contains numbers, PlaceContext charts it
automatically and folds it into your reports (see *Charts and reports*).

## How your code talks to PlaceContext

Every job follows the same simple contract:

- **Input comes in on standard input.** Read all of it and parse it (it's usually JSON). If you
  give the job no input, it runs once with an empty `{}`.
- **Your result goes out on standard output.** Print it — that's what gets saved as the artifact.
- **Keep logs and diagnostics separate** by printing them to standard error, so they don't
  corrupt the result.
- **Files you add to the job are available to your code**, read-only.
- **There's no network access** unless you turn it on — leave it off unless a job genuinely needs
  to reach the internet.
- **There's a time limit** — 5 minutes by default, raisable up to an hour per job.
- **Nothing is installed for you.** No `pip install` step. If your code needs a library, include
  it in the job's files, or bring your own container image for heavier dependencies.

## Pick a language

**Python is the default** — it reads best for the data-shaping work jobs usually do, and its
built-in library handles JSON, CSV, and dates without anything extra. You can also write jobs in
Node.js, Go, Ruby, or .NET. Each language has a sensible default file name (`main.py`,
`index.js`, `main.go`, `main.rb`, `main.cs`) so you can usually just paste code and go.

## A full example

Here's a Python job that totals up orders per day and prints a chartable result:

```python
import sys, json, os

# 1. Read the input from standard input.
data = json.loads(sys.stdin.read() or "{}")

# 2. Secrets you stored in the Vault arrive as environment variables — never hard-code them.
api_key = os.environ.get("API_KEY")

# 3. Do the work.
totals = {}
for order in data.get("orders", []):
    day = order.get("day", "unknown")
    totals[day] = totals.get(day, 0) + order.get("amount", 0)

# 4. Print the result. A map of numbers like this charts automatically.
print(json.dumps({"totals": totals}))

# 5. Logs go to standard error, so they don't pollute the result.
print(f"processed {len(data.get('orders', []))} orders", file=sys.stderr)
```

Give it this input:

```json
{"orders":[{"day":"mon","amount":12},{"day":"tue","amount":31},{"day":"mon","amount":5}]}
```

and the artifact is `{"totals": {"mon": 17, "tue": 31}}` — which shows up as a bar chart in the
run detail, in the run history, and on the Reports page.

## Create a job

### In the portal

Open the project's **Jobs** tab and click **+ New job**. Then choose how the job's code arrives:

- **Inline code** — pick a language, paste your source, and (optionally) name the entry file.
  Code jobs get a **⌁ Editor** button for a full editing page.
- **Container image** — bring your own image (e.g. `myorg/worker:latest`) for dependency-heavy
  work.

From there you can set the input, environment values, how many copies run in parallel, whether
network access is allowed, and which post-run outputs to generate.

### With an AI agent

An agent can author and upload a job for you in one call — see *MCP and agents* for the full
walkthrough. New jobs an agent creates come with charting turned on, so they produce a chart
from their very first run.

## Give a job secrets and settings

- **Plain settings** go in the job's environment as `KEY=VALUE` lines.
- **Secrets** — API keys, passwords — belong in the project **Vault**. They're encrypted, can't
  be read back in the UI, and are handed to every run as environment variables. Reference them by
  name in your code; never paste the actual value into job code or settings. The job editor shows
  which vault secrets will be available.

## Run a job

| From | How |
|---|---|
| **Portal** | The **Run** button on the job card. The run then shows up in the run history below |
| **TUI** | Select the job and press **`[R]`**. Press `⏎` to drill into the run history and any run's detail |
| **On a schedule** | Add a schedule trigger with a cron expression, e.g. `0 0 * * *` for daily at midnight |
| **On an event** | Add an event trigger so the job fires whenever something happens — another job finishing, a change being recorded, or an event you define yourself |

Schedules and event triggers are managed on the **Jobs** tab (add, pause, delete). Each firing
starts its own run, and several runs can go at once.

## Run more with shards and reduce

- **Multiple inputs** — put one JSON document per line as the job's input, and each line runs as
  its own parallel copy (up to your chosen limit, 1–32).
- **A reduce step** — an optional second stage that receives all those results and combines them
  into one final artifact, shown separately in the run detail.
- **Prompted parameters** — instead of raw input, a job can declare named parameters. Running it
  then asks you for the values.

## Every run generates an artifact — declared by the return type

Each job declares a **return type**: what its code prints on stdout. That type determines the
artifact generated for every run — artifact generation is mandatory, so a completed run always
has at least one stored, openable output (if the typed build ever fails, the raw result is stored
instead):

| Return type | The artifact every run gets |
|---|---|
| **JSON** | The result stored verbatim as `result.json` |
| **Table** | The result rendered as a clean, self-contained HTML report |
| **Chart** | The result rendered as a chart page |
| **HTML** | The returned document stored openable as-is |
| **CSV** | The result flattened into a downloadable spreadsheet |
| **Text** | The result stored verbatim as `result.txt` |
| **PDF** | The PDF the job wrote to `/out`, stored openable as-is |
| **Image** | The image the job wrote to `/out` (png/jpg/gif/webp/svg) |
| **Video** | The video the job wrote to `/out` (mp4/webm/mov/…) |

Pick the return type in the portal editor (Outputs section). Jobs an agent creates via code
upload default to **Chart** — jobs exist to produce results worth looking at.

On top of that, you can turn on extra post-run outputs (chart, HTML report, CSV, raw bundle of
every produced file). They're saved and linked on the run, and a failing one never fails the run
itself. Press **`[s]`** on a job in the TUI to toggle them (along with network access and the
time limit).

## Read the results

A run records, for each copy: whether it succeeded, the artifact it produced, and its log — plus
the combined reduce result if you used one. The portal pretty-prints JSON artifacts, charts any
numbers, and lists the extra outputs (report, chart, CSV, bundle) as links. In the TUI, the same
run shows the numbers as an ASCII chart, and `[o]` / `[1–9]` open any links in the output.

To find an old result by meaning rather than date, search across a project's run outputs from an
agent — it does a semantic search over everything your jobs have produced.
