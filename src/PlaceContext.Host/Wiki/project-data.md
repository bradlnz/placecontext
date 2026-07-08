# Project data

*Every project has its own database — real SQL over a private, isolated Postgres schema, with a Monaco editor in the portal's Data tab.*

## The idea

The **Data** tab (`/project/<id>/data`) gives each project a genuine relational database of its
own: create tables, store data, and query it with standard PostgreSQL SQL. It is not a toy grid —
it's a private schema inside the cluster's Postgres, provisioned lazily on first use, with the
isolation enforced by Postgres itself.

## How isolation works

For a project with id `4f2e…`, the platform provisions (idempotently, on first use):

- a schema named `proj_4f2e…` (the guid without dashes), and
- a matching **NOLOGIN role** of the same name.

Every statement you run executes inside a transaction that first pins:

```sql
SET LOCAL ROLE "proj_<guid>";
SET LOCAL search_path TO "proj_<guid>";
SET LOCAL statement_timeout = '10s';
```

The role holds privileges on **its own schema only** — full rights on its tables and sequences
(including default privileges for future ones), and `REVOKE ALL ON SCHEMA public`. So:

- a project **cannot read another project's tables** — different schema, different role, no grant;
- a project **cannot touch the platform's tables** — those live in `public`, where the project
  role has no table grants;
- Postgres enforces this, not application code. There is no query rewriting to bypass.

## The editor

- **Monaco SQL editor** with highlighting (falls back to a basic editor if the Monaco CDN is
  unreachable — SQL still runs either way).
- **Tables sidebar** — every table in your schema with an approximate row count (`~N`, from
  Postgres planner estimates, so it lags a little behind reality). Click a table to run
  `SELECT * FROM "table" LIMIT 100`. `↻` refreshes.
- **▶ Run (SQL)** executes what's in the editor.

## Result semantics and limits

| Behaviour | Detail |
|---|---|
| Row cap | Result grids are truncated at **500 rows** (marked "truncated at 500") |
| Statement timeout | **10 seconds** per execution — a runaway query is cancelled by Postgres |
| Multiple statements | Allowed; run in one transaction. When several return rows, **the last result set wins** |
| DDL/DML | Shows `OK — N row(s) affected`; the tables sidebar refreshes automatically |
| NULLs | Rendered as `∅` |
| Errors | The Postgres error message is shown next to the Run button |

## Worked example

Paste and run each step (or all at once — remember: last SELECT wins for display):

```sql
-- 1. A table for sensor readings
CREATE TABLE readings (
    at     timestamptz DEFAULT now(),
    sensor text        NOT NULL,
    value  numeric     NOT NULL
);
```

```sql
-- 2. Some data
INSERT INTO readings (sensor, value) VALUES
    ('door',   21.5),
    ('door',   22.1),
    ('window', 19.8),
    ('window', 20.2),
    ('roof',   25.0);
```

```sql
-- 3. Aggregate it
SELECT sensor,
       count(*)              AS samples,
       round(avg(value), 2)  AS avg_value,
       max(value)            AS max_value
FROM readings
GROUP BY sensor
ORDER BY avg_value DESC;
```

The result grid shows one row per sensor; `readings` now appears in the sidebar with its row
estimate. Standard PostgreSQL applies throughout — indexes, views, CTEs, window functions,
`timestamptz` arithmetic — subject to the 10 s timeout and your schema-scoped privileges.

## Pairing jobs with the Data tab

A powerful pattern: **jobs generate the data, the Data tab is where you interrogate it.**

- A scheduled job fetches or computes something every night (egress enabled if it calls an API),
  emits the records as its JSON artifact, and a follow-up step loads them into project tables.
- The Data tab then becomes the project's analytical surface: ad-hoc `GROUP BY`s over everything
  the jobs have accumulated, not just the latest run's artifact.
- Conversely, a run's chartable summary (see *Charts and reports*) is great for the glanceable
  trend, while the Data tab holds the full-fidelity rows behind it.

Keep the shapes boring and SQL-friendly — one row per fact, timestamps as `timestamptz`, numbers
as `numeric` — and the 500-row/10-second limits will rarely matter, because you'll be selecting
aggregates rather than raw dumps.

## More SQL that works well here

Everything ordinary PostgreSQL offers is available inside your schema:

```sql
-- An index for the queries you actually run
CREATE INDEX readings_sensor_at ON readings (sensor, at DESC);

-- A view to save retyping the aggregate
CREATE VIEW sensor_summary AS
SELECT sensor, count(*) AS samples, round(avg(value), 2) AS avg_value
FROM readings GROUP BY sensor;

SELECT * FROM sensor_summary ORDER BY avg_value DESC;
```

```sql
-- Window functions for trends
SELECT at::date AS day, sensor, value,
       avg(value) OVER (PARTITION BY sensor ORDER BY at
                        ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS rolling_avg
FROM readings
ORDER BY sensor, at;
```

Since your role owns the tables it creates, maintenance statements work too — `ANALYZE readings`
freshens the planner statistics that feed the sidebar's `~N` row estimates.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `canceling statement due to statement timeout` | The 10 s cap fired. Narrow the query (add a `WHERE`, select aggregates, add an index) |
| `permission denied for schema public` | Working as intended — your role has no rights outside its own schema. Create the table in your schema (no prefix needed; `search_path` is pinned) |
| Results look cut off | The 500-row display cap — the banner says "truncated at 500". Aggregate instead of dumping raw rows |
| Only the last query's rows showed | Multiple statements ran fine, but the grid shows the **last** result set. Run SELECTs one at a time to inspect each |
| Row estimate looks stale | It's a planner estimate. `ANALYZE <table>;` refreshes it |
| "basic editor (Monaco CDN unreachable)" | The rich editor couldn't load — cosmetic only; SQL still executes |

## Operational notes

- Provisioning is **lazy and idempotent** — the schema/role appear the first time the project's
  Data tab (or data API) is used; re-running provisioning is harmless.
- The per-project schemas live in the same Postgres the platform uses, so they are covered by the
  same **nightly dump** (`pctl db backup-now`, `pctl db backups`, `pctl db restore`) and by
  `pctl db ha` replication when enabled.
- Schema names are derived from the project GUID (`proj_` + 32 hex chars), safely inside
  Postgres's 63-character identifier limit.
