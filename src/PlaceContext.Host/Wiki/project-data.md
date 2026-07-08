# Project data

*Every project gets its own private database — create tables, run SQL, and export results, all from the Data tab.*

## What the Data tab gives you

Open a project and click **Data**. You get a real relational database that belongs to this
project alone: create tables, store rows, and query them with standard SQL. It's not a toy grid —
it's a genuine database, and it's **completely private**. One project can never see another
project's tables, and no project can touch the platform's own data.

There's nothing to set up. The database appears the first time you use the Data tab.

## Create a table

Two ways, whichever you prefer.

### The New table wizard

Click **New table**, give it a name, and add your columns:

1. Name each column and pick its type (text, number, timestamp, boolean, and so on).
2. Mark a column as the **primary key** if it uniquely identifies a row.
3. Mark columns **not null** where a value is always required.
4. Create it — the table appears in the sidebar, ready for data.

### Or write the SQL yourself

Prefer SQL? Just write it in the editor and run it:

```sql
CREATE TABLE readings (
    at     timestamptz DEFAULT now(),
    sensor text        NOT NULL,
    value  numeric     NOT NULL
);
```

## Run a query

Type SQL into the editor and press **▶ Run**. A few things to know:

- Click any table in the sidebar to instantly see its first rows.
- Results show up to **500 rows** at a time — if there are more, aggregate rather than dumping
  everything.
- A single query has up to **10 seconds** to run, so a runaway query gets stopped rather than
  hanging.
- You can run several statements at once; the last one that returns rows is what's displayed.
- Empty values show as `∅`. If something's wrong, the database's error message appears right next
  to the Run button.

## Rename, drop, and refresh

The sidebar lists every table with an approximate row count. From there you can **rename** a
table or **drop** one you no longer need. The `↻` button refreshes the list — handy after your
jobs have loaded new data.

## Export to CSV

Any table or query result can be exported to **CSV** for a spreadsheet or to share. Run the
query (or open the table), then export the result.

## Worked example

Paste and run these in turn:

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

You get one row per sensor, and `readings` now shows in the sidebar. Everything standard SQL
offers works here — indexes, views, CTEs, window functions, date arithmetic:

```sql
-- An index for the queries you actually run
CREATE INDEX readings_sensor_at ON readings (sensor, at DESC);

-- A view so you don't retype the aggregate
CREATE VIEW sensor_summary AS
SELECT sensor, count(*) AS samples, round(avg(value), 2) AS avg_value
FROM readings GROUP BY sensor;

SELECT * FROM sensor_summary ORDER BY avg_value DESC;
```

```sql
-- Window functions for rolling trends
SELECT at::date AS day, sensor, value,
       avg(value) OVER (PARTITION BY sensor ORDER BY at
                        ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS rolling_avg
FROM readings
ORDER BY sensor, at;
```

## A great pattern: jobs write, the Data tab reads

Jobs and the Data tab work beautifully together:

- A scheduled job pulls or computes something each night, prints the records as its result, and
  loads them into a project table.
- The Data tab then becomes your analysis surface — run ad-hoc `GROUP BY`s over everything the
  jobs have piled up, not just the latest run.
- A job's chartable summary (see *Charts and reports*) is perfect for the glanceable trend; the
  Data tab holds the full detail behind it.

Keep your tables simple — one row per fact, timestamps as `timestamptz`, numbers as `numeric` —
and select aggregates rather than raw dumps, and the row and time limits will rarely get in your
way.

## Troubleshooting

| What you see | What it means |
|---|---|
| `canceling statement due to statement timeout` | The query took over 10 seconds — narrow it with a `WHERE`, select aggregates, or add an index |
| `permission denied for schema public` | You're trying to work outside your project's own space. Just create the table without any prefix — it lands in the right place |
| Results look cut off | You hit the 500-row display limit — aggregate instead of dumping raw rows |
| Only the last query's rows showed | Several statements ran, but only the last result set is displayed. Run SELECTs one at a time to see each |
| Row count looks stale | It's an estimate; run `ANALYZE <table>;` to refresh it |

## Backups

Your project's data is backed up along with everything else — the nightly backup covers it
automatically, and an operator can restore from it when needed (see *Cluster and nodes*).
