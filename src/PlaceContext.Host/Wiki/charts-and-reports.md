# Charts and analytics

*Print JSON with numbers in it and your job charts itself — and build your own SQL-backed charts over any table, on the Analytics tab and the Dashboard.*

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
- A mixed object charts its numeric entries and ignores the rest.
- Anything that isn't a recognisable series simply shows the raw result with no chart.

The portal draws clean charts that follow your light/dark theme; the TUI draws the same data as
an ASCII chart right in a run's detail.

## Where your job's charts show up

### In the run detail

Open any run under **Observability** and you see each result pretty-printed, with a chart right
beneath it whenever the result is a series of numbers.

### In the Artifacts viewer

Every run's result is stored as an openable artifact. The **Artifacts** page is a file viewer
over all of them across every project: JSON pretty-prints, CSV renders as a table, charts and
HTML reports open as-is, and repeated outputs from the same job are grouped with a **version
dropdown** so you can step back through history.

### On the Dashboard

The **Dashboard** shows a read-only copy of the current project's SQL charts (below) plus pinned
entity views with mini distribution bars — a glanceable summary without opening Analytics.

### In the TUI

Select a job, press `⏎` for its run history, and `⏎` again for a run. Results and logs render
with ASCII charts for any numbers.

## Build your own charts with SQL

Beyond the automatic charts, you can define your own over any table in the project. On the
**Data → Analytics** tab, click **＋ SQL chart**, give it a name, and write a `SELECT` in the
Monaco editor:

```sql
SELECT suburb, count(*) FROM sites GROUP BY 1 ORDER BY 2 DESC LIMIT 10
```

The first column becomes the labels, the remaining numeric columns become the series. Each chart
can be switched between **bar**, **line**, and **pie** with one click, refreshed, or deleted.
Charts are **SELECT-only and isolated to the project** — they can never touch another project's
data or run anything that writes.

Every **entity** gets the same Analytics tab over its own table, and seeds a few sensible charts
automatically the first time you open it (see *Entities and insights*).

> An AI agent can build these for you too — it has safe, SELECT-only tools to query a project's
> data and save charts. See *MCP and agents*.

## Worked example: one result, three places

A job prints:

```json
{"totals": {"mon": 17, "tue": 31, "wed": 9}}
```

Here's where it lands:

1. **Run detail** — the result is pretty-printed with a bar chart (`mon`, `tue`, `wed`) beneath.
2. **Artifacts** — stored as `result.json`, openable any time, versioned against earlier runs.
3. **TUI** — pressing `⏎` into the run shows it as an ASCII chart:

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
| Nothing on the Analytics chart | The `SELECT` returned no rows, or no numeric column after the first | Return a label column plus at least one number |

## Make your data chart well

- Print a **small labelled series** — a handful of keys reads far better than 500 rows. Aggregate
  before you print.
- Use one of the recognised number keys (`total`, `count`, `value`, …) in object rows.
- Keep the result **pure JSON on standard output** — send every diagnostic message to standard
  error, or it breaks parsing and nothing charts.
- For SQL charts, put the label in the first column and `GROUP BY` / aggregate so the series stays
  short and readable.

## In short

- Numbers in your JSON result chart themselves — no setup — and land in the run detail, the
  Artifacts viewer, and the TUI.
- Build your own **SQL charts** over any table on the Analytics tab, switchable between bar, line,
  and pie, and mirrored read-only on the Dashboard.
- Every entity comes with its own analytics, seeded automatically.
