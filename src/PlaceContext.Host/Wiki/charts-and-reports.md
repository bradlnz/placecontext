# Charts and reports

*Print JSON with numbers in it and your job charts itself — in the run history, the run detail, the Reports page, and even the TUI.*

## Get a chart for free

Print JSON from your job and PlaceContext looks at the shape. Any of these count as a series of
numbers and become a bar chart with no setup at all:

| Your result looks like | Example | You get |
|---|---|---|
| A list of numbers | `[3, 1, 4]` | One bar each |
| A list of objects | `[{"day":"mon","total":12}, {"day":"tue","total":31}]` | One bar per object |
| A map of numbers | `{"mon":12, "tue":31}` | A bar per key, sorted by key |
| A wrapper around one of these | `{"totals":{"mon":12,"tue":31}}` | Unwrapped, then charted |

A few things worth knowing so your data charts cleanly:

- For a list of objects, name the label field something like `label`, `name`, `day`, or `date`,
  and the number field something like `value`, `count`, `total`, or `amount`. PlaceContext picks
  those up automatically.
- You need **at least two** numbers — a lone count isn't a trend.
- At most **16 bars** are drawn, so aggregate big data before printing it.
- A mixed object charts its numeric entries and ignores the rest.
- Anything that isn't a recognisable series simply shows the raw result with no chart.

The portal draws clean bar charts that follow your light/dark theme; the TUI draws the same data
as an ASCII chart right in a run's detail.

## Where your charts show up

### In the run history

If a job has the **Chart** output turned on (it's on by default), every run gets a chart drawn
from that run's data — titled, labelled, with the values printed on it. The newest run's chart is
expanded on the Jobs page; older ones open on demand.

### In the run detail

Open any run and you see each result pretty-printed, with a bar chart right beneath it whenever
the result is a series of numbers. The extra outputs (report, chart, CSV, raw bundle) are listed
at the top as links.

### On the Reports page

The **Reports** page ends with a **Job data** section — a rolling view over your last 24 runs
across all projects:

- **Stat tiles** — how many runs came in, what share succeeded, how many produced output, and how
  many chartable series were found.
- **A written summary** — a couple of plain sentences describing how your jobs have been doing:
  failures, gaps, unusually busy jobs. It loads in the background; if it's not there yet, that's
  fine.
- **Chart cards, grouped by job** — every chartable result becomes a card linked back to its job.

### In the TUI

Select a job, press `⏎` for its run history, and `⏎` again for a run. Results and logs render
with ASCII charts for any numbers. Press `[o]` to open the first link in the output, or `[1–9]`
for the nth (handy for opening a stored report or chart).

## Generate a report for a project

The top of the **Reports** page builds a full written report for one project from everything it
knows — its context, decisions, activity, and risk.

1. Pick a **project** and a **template** (the built-in *Onboarding Brief*, or one your team has
   defined).
2. Click **Generate**. If the local AI model is available the prose is polished; otherwise you
   still get a clean, structured report.

The report opens with a chart counting its recommended actions by severity (critical, high,
medium, low, info), each one listed beneath, followed by the full write-up.

You can also define your own report templates (from an agent) — give it a name, a description,
and the sections to include, choosing from Overview, Context, Requirements, Decisions,
Activity, Risk, Usage, and Action plan.

## Worked example: one result, four places

A job prints:

```json
{"totals": {"mon": 17, "tue": 31, "wed": 9}}
```

Here's where it lands:

1. **Run detail** — the result is pretty-printed with a bar chart (`mon`, `tue`, `wed`) beneath.
2. **Run history** — the Chart output produces a titled, labelled chart under the run, newest
   expanded.
3. **Reports → Job data** — within the last-24-runs window it becomes a chart card under the
   job's name and counts toward the "chartable series" tile.
4. **TUI** — pressing `⏎` into the run shows it as an ASCII chart:

```
mon  █████████████████ 17
tue  ███████████████████████████████ 31
wed  █████████ 9
```

## Why didn't my result chart?

| What you see | Why | Fix |
|---|---|---|
| No chart anywhere | The result isn't valid JSON — often a stray `print()` on standard output | Move diagnostics to standard error |
| Valid JSON, still no chart | No recognisable series: fewer than 2 numbers, or object rows with no numeric field | Give rows a `value`/`count`/`total` field; keep the numbers near the top level |
| Only some bars | More than 16 points | Aggregate before printing |
| Chart output missing | The Chart output isn't turned on for that job | Turn it on in the editor, or `[s]` in the TUI |

## Make your jobs chart well

- Print a **small labelled series** — a handful of keys reads far better than 500 rows (only 16
  bars are drawn anyway). Aggregate before you print.
- Use one of the recognised number keys (`total`, `count`, `value`, …) in object rows.
- Keep the result **pure JSON on standard output** — send every diagnostic message to standard
  error, or it breaks parsing and nothing charts.
- Turn on the **Chart** output for a richer, composed visual on top of the automatic one.

## Save a chart or report to a file

Every generated chart, HTML report, CSV, and bundle is linked on its run, so you can open it from
the portal's run detail with a click. If you'd rather grab one from the command line — to attach
it to an email, say, or open a chart page in your browser:

```bash
pctl minio open placecontext-reports/<key>   # download an output and open it
```

The `<key>` is shown on the run's output links. This is handy when you want the finished artifact
in hand rather than viewed inside the portal.

## In short

- Numbers in your JSON result chart themselves — no setup.
- The same chart shows up in the run history, the run detail, the Reports page, and the TUI.
- Turn on the **Chart** output for a polished visual, and **Generate** a full report on the
  Reports page whenever you want the whole project written up.
