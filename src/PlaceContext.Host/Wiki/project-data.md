# Project data

*Query project tables and map job results into them.*

## Tables

Each project has an isolated SQL workspace. Open **Data → Tables** to:

- create a table with the table builder;
- run SQL in the editor;
- browse table rows;
- rename or remove tables;
- export query results to CSV.

Project database roles keep one project's tables separate from another project's data and from
PlaceContext's system tables.

## Data map

The **Data map** connects job or chain output to a project table.

1. Drag the canvas to arrange jobs, chains, and tables.
2. Drag a source's **+** handle onto a table, or click **New mapping**.
3. Choose the row path and map output fields to typed columns.
4. Enable the mapping.

After a completed run, matching rows are appended to the table. PlaceContext adds ingestion time
and run ID columns for traceability. A new target table is created on first ingest.

Use **Suggest from last run** to prefill fields for a job with a recent result.

## Good practice

Use stable column names, store timestamps with timezones, keep one row per fact, and aggregate
large datasets in SQL rather than loading every row into the UI.
