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

## Use project data from another tool

A personal API token lets an approved script or integration read entity records and search the
selected project. Create one under **Settings → API tokens**. The secret is shown once, so copy it
when it appears.

Every request needs the token and the project name or ID. For example:

```bash
curl -H "Authorization: Bearer pct_your_token" \
     -H "X-Project: storefront-api" \
     "https://your-workspace.placecontext.ai/api/v1/search?q=customer&limit=25"
```

Useful read endpoints are:

- `GET /api/v1/entities` to list the project's registered entities;
- `GET /api/v1/{entity-name}` to read a page of entity records;
- `GET /api/v1/search?q={term}` to search project activity, decisions, artifacts, entity tags,
  indexed content, and connected search data.

Search terms must contain 2–200 characters. `limit` defaults to 25 and accepts 1–100. Results only
include the project selected by `X-Project-Id` or `X-Project`. The token must belong to a user with
permission to read project data.
