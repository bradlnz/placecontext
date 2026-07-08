# Charts and reports

*When a job's artifact is JSON with a numeric series, it charts automatically — in the run history, the run detail, the global Reports page, and even as ASCII in the TUI.*

## What charts automatically

Emit JSON from your job (on STDOUT) and the platform inspects it. These shapes are understood as
a **numeric series** and become bar charts, no configuration needed:

| Shape | Example | Rendered as |
|---|---|---|
| Array of numbers | `[3, 1, 4]` | Indexed bars (`0`, `1`, `2`, …) |
| Array of objects | `[{"day":"mon","total":12}, {"day":"tue","total":31}]` | One bar per object — label field + value field |
| Numeric map | `{"mon":12, "tue":31}` | Key/value bars, sorted by key |
| Container object | `{"totals":{"mon":12,"tue":31}}`, `{"data":[…]}`, `{"series":[…]}`, `{"rows":[…]}` | Unwrapped one level, then as above |

Details worth knowing:

- **Field detection** for object rows: the label comes from the first of
  `label, name, key, date, day, month, hour, id, title`; the value from the first of
  `value, count, total, amount, sum, avg, n, qty`. A value field is required; the label falls
  back to the row index.
- **Tolerant maps** — a mixed object like `{"bookings":3,"sessions":3,"byStatus":{…}}` still
  charts: the numeric entries are used, the rest ignored.
- **Two points minimum** — a single stray count isn't a series. At most 16 rows are drawn.
- Anything unchartable renders nothing; the raw artifact block is always shown regardless.

The portal draws these as inline SVG bar charts using the theme's own tokens (they follow
light/dark automatically). The TUI implements the **same shape rules** and renders the series as
a deterministic ASCII bar chart (`█` bars with the value printed beside each) directly in a run's
detail view — the two implementations are deliberately kept in sync.

## Where charts appear

### 1. Inline in the run history (the LLM-drawn Chart artifact)

Jobs with the **Chart** post-job action (on by default for jobs created via `upload_job_code`)
get a chart generated after every run: the run's data is handed to the **local LLM** (Gemma via
Ollama) with instructions to draw ONE self-contained inline-SVG chart — titled, labelled, and
with the actual values printed on it. If the LLM is disabled or returns something unusable, a
deterministic fallback chart is stored instead.

The result is stored in the object store and embedded directly in the Jobs page's run-history
panel — the newest run's chart is expanded, older ones expand on demand. The chart document is
embedded in a **sandboxed iframe with scripts disabled**: the LLM-generated document can render
but can never execute code.

### 2. In the run detail

Opening a run shows every shard's artifact pretty-printed, and — when the artifact parses as a
numeric series — the deterministic SVG bar chart right below it, for shard results and the reduce
result alike. Post-job outputs (HTML report, chart, CSV, raw bundle) are listed at the top as
links with their sizes.

### 3. The global Reports "Job data" section

The **Reports** page ends with *Job data* — the global reporting view over the last **24 runs
across all projects**:

- **Stat tiles** — runs ingested, % succeeded (colour-coded), jobs producing output, and how many
  chartable series were found.
- **Agent summary** — the local LLM narrates the period in two or three plain sentences (how the
  jobs have been doing, failures, gaps, unusually busy jobs). It loads in the background and its
  absence is never an error.
- **Chart cards, grouped by job** — every chartable JSON artifact from those runs becomes a card
  (up to 12 cards, at most 2 per run, identical shard outputs deduplicated), labelled with its
  source (`shard 0`, `reduce`, or a file name) and linked back to the job.

### 4. The TUI

Select a job on the dashboard, `⏎` for its run history, `⏎` again for a run: shard artifacts,
errors, and logs render as Markdown with **ASCII bar charts** for any numeric series. Links found
in the output are collected — `[o]` opens the first, `[1–9]` the nth (handy for the MinIO-stored
report/chart links).

## Defined reports

The top of the Reports page generates a **defined report** for one project from its accumulated
data: context, requirements, decisions, work items, activity, risk, usage.

1. Pick a project and a template (the built-in **Onboarding Brief**, or any template your
   workspace has defined).
2. Optionally tick *queue action plan as work items*.
3. **Generate** — if the local LLM is available the prose is polished (`LLM-polished` badge);
   otherwise a deterministic Markdown report is produced.

The report renders with:

- an **action-plan severity chart** — proportional bars counting actions by severity
  (critical / high / medium / low / info), each action listed beneath with its severity;
- the full report body rendered from Markdown.

Custom templates are defined over MCP:

```text
define_report_template
  name:        "Weekly Ops Review"
  description: "What changed, what's risky, what to do next"
  sources:     ["Overview", "Activity", "Risk", "ActionPlan"]
```

`sources` is an ordered list of section kinds — choose from `Overview`, `Context`,
`Requirements`, `Decisions`, `WorkItems`, `Activity`, `Risk`, `Usage`, `ActionPlan`.
`list_report_templates` shows what exists; `generate_report` produces one from an agent.

## Opening stored outputs from the CLI

Post-job outputs live in MinIO. Two `pctl` helpers:

```bash
pctl minio open placecontext-reports/<key>   # download an object and open it (e.g. a chart.html)
pctl db minio                                # port-forward the MinIO console to localhost:9001
```

## Worked example: one artifact, four surfaces

A python job prints this artifact:

```json
{"totals": {"mon": 17, "tue": 31, "wed": 9}}
```

What happens:

1. **Run detail (portal)** — the artifact is pretty-printed, and because `{"totals":{…}}`
   unwraps to a numeric map, an SVG bar chart (`mon`, `tue`, `wed`) renders directly beneath it.
2. **Run history (portal)** — if the job has the Chart action, the local LLM receives the JSON
   and produces `chart.html` (a titled, labelled inline-SVG chart with the values printed on it),
   stored in MinIO and embedded under the run, newest run expanded.
3. **Reports → Job data** — within the next 24-run window, the series becomes a chart card under
   the job's name, counted in the "chartable series" tile and folded into the LLM's period
   narrative.
4. **TUI** — `⏎` into the run shows the same series as an ASCII chart:

```
mon  █████████████████ 17
tue  ███████████████████████████████ 31
wed  █████████ 9
```

## Why didn't my artifact chart?

| Symptom | Likely cause |
|---|---|
| No chart anywhere | The artifact isn't valid JSON — a stray `print()` on stdout corrupted it. Move diagnostics to stderr |
| Valid JSON, still no chart | No recognisable series: fewer than 2 numeric points, object rows without a numeric `value/count/total/…` field, or the series is nested deeper than one container level |
| Chart shows odd subset | Tolerant-map behaviour: only the numeric top-level entries chart; nested objects beyond one unwrap are ignored |
| Only some bars | More than 16 points — the renderer caps at 16 rows. Aggregate before emitting |
| Chart post-job output missing | The action isn't enabled on the job, or the object store is disabled — check the job's settings (`[s]` in the TUI) and `pctl status` for MinIO |

## Making your jobs chart well

- Prefer a **small labelled series** over a giant blob: `{"totals": {...}}` with a handful of
  keys reads better than 500 rows (only 16 bars are drawn).
- Use one of the recognised value keys (`total`, `count`, `value`, …) in object rows.
- Keep the artifact pure JSON on stdout — diagnostic prints belong on stderr, or they break
  parsing and nothing charts.
- Turn on the **Chart** post-job action for a richer, LLM-composed visual on top of the
  deterministic inline chart. Toggle it in the portal editor or with `[s]` in the TUI.
