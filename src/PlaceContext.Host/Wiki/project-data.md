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

Tables are the source of truth for ingested and processed data. The Data sidebar exposes tables
and saved queries; search and vector indexes are derived retrieval representations, not a separate
data catalogue. Removing the index browser does not delete existing search data.

## Live charts

Open **Data → Analytics** and add a **SQL chart**. Return a label column and numeric values;
aggregate and order the results in SQL before charting. Saving reads the current editor contents.
The chart query runs in a read-only project transaction with a 10-second statement timeout and a
500-row result limit; charts display up to 24 labels.

Analytics and the dashboard's **Charts** view refresh queries every 10 seconds while open. This is
polling, so changes appear on the next completed refresh. In Analytics, turn off **Live** to pause
updates or select **Refresh now**. Refreshes do not rewrite saved chart definitions. Query failures
show an error and the saved snapshot; other charts continue updating. Older snapshots without SQL
must be edited and given a query before they can update live.

**Table previews** use sampled rows and are not totals. Use a SQL aggregate chart for reporting over
the whole dataset.

## Data map

The **Data map** connects job or workflow output to a project table.

1. Drag the canvas to arrange jobs, workflows, and tables.
2. Drag a source's **+** handle onto a table, or click **New mapping**.
3. Choose the row path and map output fields to typed columns.
4. Enable the mapping.

After a completed run, matching rows are appended to the table. PlaceContext adds ingestion time
and run ID columns for traceability. A new target table is created on first ingest.

Use **Suggest from last run** to prefill fields for a job with a recent result.

## Data graph

Open **Data → Graph** to follow project relationships between tables, entities, jobs, runs,
decisions, and artifacts. Clicking a previewable artifact or attachment renders it in the graph
detail panel, so supporting files can be inspected without leaving the graph.

## Good practice

Use stable column names, store timestamps with timezones, keep one row per fact, and aggregate
large datasets in SQL rather than loading every row into the UI.

## Use project data from another tool

Jobs can query any table or view in their project through the read-only data API, without registering
it as an entity. Map and reduce processes receive `PC_PROJECT_ID`, `PC_JOB_ID`, and `PC_RUN_ID` for
the actual execution; these values override job environment values with the same names.

Enable project data access for the map or reduce process (`PC_USE_PROJECT_DATA=true`). Starting
the job requires `data.read`. The opted-in process receives a transient `PC_DATA_TOKEN` scoped to
its tenant, project, job, and run; it is not saved into the run snapshot. Access ends when the run
finishes, its heartbeat becomes stale, or the credential expires (at most 24 hours).

Configure `PC_HOST` with a worker-reachable portal URL and permit access to it in the job's network
policy; network-disabled jobs cannot use this opt-in. Use HTTPS in production. Jobs do not need a
personal token, database credentials, or PII encryption keys for this API.

- `GET /api/v1/job-data/tables` lists the authenticated run's project tables and views.
- `POST /api/v1/job-data/query` accepts `{"sql":"SELECT …"}`. The credential determines the project;
  caller-supplied project IDs cannot select a different project.

For integrations outside a job, a personal API token with `data.read` also supports:

- `GET /api/v1/projects/{projectId}/data/tables` lists tables and views.
- `POST /api/v1/projects/{projectId}/data/query` accepts `{"sql":"SELECT …"}` and returns
  `columns`, `rows`, and `truncated`. It uses the same read-only transaction, timeout, and 500-row
  limit as live charts. Writes and transaction-control statements are rejected.

For example, a Python job can make a decision from an aggregate rather than copying customer rows:

```python
import json
import os
import urllib.request

url = f"{os.environ['PC_HOST'].rstrip('/')}/api/v1/job-data/query"
request = urllib.request.Request(url, method="POST", headers={
    "Authorization": f"Bearer {os.environ['PC_DATA_TOKEN']}",
    "Content-Type": "application/json",
}, data=json.dumps({"sql": "SELECT count(*) AS paid_orders FROM orders WHERE status = 'paid'"}).encode())
with urllib.request.urlopen(request, timeout=15) as response:
    result = json.load(response)
if result["truncated"]:
    raise RuntimeError("Refine the query before making a decision from incomplete data.")
count = int(result["rows"][0][0])
print(json.dumps({"action": "process_orders" if count > 0 else "wait", "paid_orders": count}))
```

Both APIs currently permit decrypted results across the selected project's tables; separate PII
field grants and masking are not enforced. Personal tokens inherit their user's workspace permissions
and are not restricted to a run or individual tables. A project ID environment variable does not
restrict a personal token's authority. Use the transient job credential for opted-in workers, and
do not print credentials or sensitive rows into job output.

The host emits metadata-only access events for both APIs, including authorization denials. New
MCP `query_project_data` and `save_sql_chart` Inspector traces omit SQL, chart names, returned data,
and database error details; the actual tool response is unchanged. Existing Inspector history is
not rewritten. See [Setup](/wiki/setup) for audit collection requirements.

A personal API token lets an approved script or integration read entity records and search the
selected project. Create one under **Settings → API tokens**. The secret is shown once, so copy it
when it appears.

Every request needs the token and the project name or ID. For example:

```bash
curl -H "Authorization: Bearer pct_your_token" \
     -H "X-Project: storefront-api" \
     "https://your-workspace.placecontext.io/api/v1/search?q=customer&limit=25"
```

Useful read endpoints are:

- `GET /api/v1/entities` to list the project's registered entities;
- `GET /api/v1/{entity-name}` to read a page of entity records;
- `GET /api/v1/search?q={term}` to search project activity, decisions, artifacts, entity tags,
  indexed content, and connected search data.

Search terms must contain 2–200 characters. `limit` defaults to 25 and accepts 1–100. Results only
include the project selected by `X-Project-Id` or `X-Project`. The token must belong to a user with
permission to read project data.
