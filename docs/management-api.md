# PlaceContext Management API (`/api/v1`)

A stable, versioned REST contract for declaratively managing **projects**, **jobs**, and
**schedules** (cron/event triggers) — built for the PlaceContext Terraform provider (and any other
IaC/CI client). It is separate from the Blazor portal (cookie auth) and the MCP server at `/mcp`
(OAuth bearer auth): the management API uses its own single-key **ApiKey** authentication scheme and
speaks plain JSON over HTTP, no protocol framing.

There is also a **personal entity data API** at `/api/v1/{entity-name}` (project via
`X-Project-Id` / `X-Project` middleware) authenticated with user API tokens from
**Settings → API tokens** (`pct_…`). See the end of this document.

- Base URL: `https://{your-workspace}.placecontext.ai/api/v1` (or `http://{workspace}.localhost:7700/api/v1`
  in local dev — the workspace is resolved from the subdomain, same as the portal/MCP).
- All request/response bodies are JSON, `camelCase` field names.
- All endpoints require authentication (see below) — there is no anonymous access.

## Authentication

Every request must carry the workspace's API key, either as a bearer token or a dedicated header:

```
Authorization: Bearer <key>
```

or

```
X-Api-Key: <key>
```

The key is a single, per-workspace admin credential configured by the operator as
`PlaceContext:Api:Key` (an environment variable / config value on the Host — not something set
through the API itself). **If no key is configured, the entire `/api/v1/*` surface returns `401` for
every request** — the management API is closed by default, never silently open.

A valid key authenticates as an admin-equivalent caller (role `Owner`, so it holds every permission
in the catalog) **scoped to the workspace resolved from the request's subdomain**. A key only ever
sees and mutates that one tenant's projects/jobs/schedules — there is no cross-tenant reach, matching
every other PlaceContext data path (the same EF Core global query filter that protects the portal and
MCP protects this API).

The key comparison is constant-time. The key is never logged. A missing, malformed, or wrong key
always returns `401 Unauthorized` (never `404`, so a caller can distinguish "wrong credentials" from
"wrong path").

## Conventions

| Concern | Convention |
|---|---|
| Field naming | `camelCase` everywhere, request and response |
| Resource ids | `Guid` (RFC 4122), rendered as a `"xxxxxxxx-xxxx-..."` JSON string |
| Timestamps | ISO-8601 with offset, e.g. `"2026-07-15T12:00:00+00:00"` |
| Create | `POST` → `201 Created` with a `Location` header pointing at the new resource's `GET` URL, body = the created resource |
| Read | `GET` → `200 OK`, or `404 Not Found` if the id doesn't resolve in this tenant |
| Update | `PUT` → `200 OK` with the updated resource |
| Delete | `DELETE` → `204 No Content` on success, `404 Not Found` if it didn't exist |
| Validation error | `400 Bad Request`, body `{ "error": "<message>" }` |
| Auth failure | `401 Unauthorized` (missing/wrong/unconfigured key) |
| Permission failure | `403` is not currently distinguished from `401` at this layer — a key always carries the full permission catalog, so a `403` would only occur for a mis-scoped/legacy caller and isn't a normal response you should expect to handle |

---

## Projects

A project is PlaceContext's top-level unit — a codebase under watch. Projects have **no delete path**
today (only an internal `Archive()` that nothing exposes end-to-end yet), so there is deliberately no
`DELETE /api/v1/projects/{id}`. Model project removal in Terraform as "remove from state" until a
delete path ships.

### `GET /api/v1/projects`

List every project in the workspace.

**Auth:** `projects.view`

**200 response:**

```json
[
  {
    "id": "8f14e45f-ceea-4b3e-8c7a-6a5b6b6d8a10",
    "name": "storefront-api",
    "path": "/home/brad/code/storefront-api",
    "status": "Registered",
    "isGraphified": false,
    "technicalRisk": null,
    "technicalRiskBand": null,
    "processRisk": null,
    "processRiskBand": null
  }
]
```

### `GET /api/v1/projects/{id}`

A single project, or `404`.

**Auth:** `projects.view`

**200 response:** same shape as one list item above.

### `POST /api/v1/projects`

Registers a project. **Idempotent by path** — POSTing a path that's already registered returns the
existing project (still `201`), so Terraform can safely retry an apply that actually already
succeeded.

**Auth:** `projects.manage`

**Request:**

```json
{
  "path": "/home/brad/code/storefront-api",
  "name": "storefront-api"
}
```

`name` is optional — when omitted, it defaults to the last path segment.

**201 response:** the created (or existing) project, `Location: /api/v1/projects/{id}`.

**400** if `path` is missing/blank.

---

## Jobs

A job is a generic map/reduce workload definition under a project: a **map step** (required) and an
optional **reduce step**, each sourced from either a pre-built container image *or* inline/multi-file
code run by a named runtime — never both for the same step.

### `GET /api/v1/projects/{projectId}/jobs`

Every job defined under the project.

**Auth:** `jobs.view`

**404** if the project doesn't exist.

**200 response:** a JSON array of the job shape shown below.

### `GET /api/v1/jobs/{id}`

A single job definition (full source), or `404`.

**Auth:** `jobs.view`

**200 response:**

```json
{
  "id": "b6e2b6a2-1e0a-4b8b-9c39-9a2f1f6a3b41",
  "projectId": "8f14e45f-ceea-4b3e-8c7a-6a5b6b6d8a10",
  "name": "nightly-report",
  "description": "Summarizes overnight orders",
  "mapSourceKind": "image",
  "mapImage": "ghcr.io/acme/report-worker:latest",
  "mapRuntimeId": null,
  "mapSource": null,
  "mapEntrypoint": null,
  "mapFiles": [],
  "inputPayloads": ["{}"],
  "mapEnv": { "REGION": "us-east-1" },
  "reduceSourceKind": null,
  "reduceImage": null,
  "reduceRuntimeId": null,
  "reduceSource": null,
  "reduceEntrypoint": null,
  "reduceFiles": [],
  "reduceEnv": null,
  "concurrencyLimit": 1,
  "successExitCodes": [0],
  "partialExitCodes": [],
  "allowNetworkEgress": false,
  "parameters": [],
  "postJobActions": ["HtmlReport"],
  "returnType": "Json",
  "returnFileName": null,
  "createdAt": "2026-07-15T12:00:00+00:00",
  "updatedAt": "2026-07-15T12:00:00+00:00"
}
```

### `POST /api/v1/projects/{projectId}/jobs`

Creates a job under the project.

**Auth:** `jobs.edit`

**404** if the project doesn't exist. **400** if the workload spec is invalid (neither an image nor a
runtime+code was given for a step, or both were).

**Request** (image-based map step, no reduce step — the minimal shape):

```json
{
  "name": "nightly-report",
  "description": "Summarizes overnight orders",
  "mapImage": "ghcr.io/acme/report-worker:latest",
  "inputPayloads": ["{}"],
  "mapEnv": { "REGION": "us-east-1" },
  "concurrencyLimit": 1,
  "successExitCodes": [0],
  "partialExitCodes": [],
  "allowNetworkEgress": false,
  "postJobActions": ["HtmlReport"],
  "returnType": "Json"
}
```

**Request** (code-based map step, multi-file):

```json
{
  "name": "etl-sync",
  "mapRuntimeId": "python",
  "mapFiles": [
    { "path": "main.py", "content": "print('hello')\n" },
    { "path": "helpers.py", "content": "def util(): ...\n" }
  ],
  "mapEntrypoint": "main.py",
  "inputPayloads": ["{\"batch\":1}", "{\"batch\":2}"],
  "concurrencyLimit": 4,
  "successExitCodes": [0],
  "partialExitCodes": [2],
  "returnType": "Table"
}
```

Full field reference (all fields except `name` are optional and default as shown):

| Field | Type | Default | Notes |
|---|---|---|---|
| `name` | string | — | required |
| `description` | string? | `null` | |
| `mapImage` | string? | `null` | pre-built container image for the map step |
| `mapRuntimeId` | string? | `null` | e.g. `"node"`, `"python"` — code workload |
| `mapSource` | string? | `null` | single-file inline source (legacy; prefer `mapFiles`) |
| `mapEntrypoint` | string? | `null` | entry filename within the runtime work dir |
| `mapFiles` | `[{path, content}]?` | `null` | multi-file source; supersedes `mapSource` when non-empty |
| `inputPayloads` | `string[]` | `[]` | one shard per entry; each is opaque to PlaceContext |
| `mapEnv` | `{string: string}` | `{}` | opaque env passed to the map container |
| `reduceImage` / `reduceRuntimeId` / `reduceSource` / `reduceEntrypoint` / `reduceFiles` / `reduceEnv` | same shapes as the `map*` fields | `null` | omit all of them for "no reduce step" |
| `concurrencyLimit` | int | `1` | must be ≥ 1 |
| `successExitCodes` | `int[]` | `[0]` | |
| `partialExitCodes` | `int[]` | `[]` | |
| `allowNetworkEgress` | bool | `false` | `false` runs containers with `--network none` |
| `parameters` | `[{name, label?, required, type, options?}]?` | `null` | inputs prompted before a manual run |
| `postJobActions` | `string[]?` | `null` | any of `HtmlReport`, `Chart`, `Csv`, `RawBundle`, `HtmlOutput` |
| `returnType` | string | `"Json"` | one of `Json`, `Table`, `Chart`, `Html`, `Csv`, `Text`, `Pdf`, `Image`, `Video` |
| `returnFileName` | string? | `null` | expected `/out` filename for `Pdf`/`Image`/`Video` return types |

**201 response:** the created job (shape above), `Location: /api/v1/jobs/{id}`.

### `PUT /api/v1/jobs/{id}`

Replaces a job's configuration. Same request body as `POST`. **404** if the job doesn't exist, `400`
on an invalid workload spec.

**Auth:** `jobs.edit`

**200 response:** the updated job.

### `DELETE /api/v1/jobs/{id}`

Permanently deletes the job definition. **Does not cascade** to its run history, artifacts, data
mappings, or triggers — those are simply orphaned (no FK relationship ties them to the job row).
Delete a job's schedules first if you want a clean removal.

**Auth:** `jobs.edit`

**204** on success, **404** if it didn't exist.

---

## Schedules

A schedule is a trigger on a job: either a **cron schedule** (`kind: "Schedule"`, with
`cronExpression`) or an **event subscription** (`kind: "Event"`, with `eventName`).

### `GET /api/v1/projects/{projectId}/schedules`

Every schedule/event trigger under the project.

**Auth:** `triggers.manage` (the permission catalog has no separate `triggers.view` — every schedule
endpoint, reads included, requires `triggers.manage`)

**404** if the project doesn't exist.

**200 response:**

```json
[
  {
    "id": "1c1a9e10-7a3a-4a5a-8b3e-9d0b0e0a2f10",
    "projectId": "8f14e45f-ceea-4b3e-8c7a-6a5b6b6d8a10",
    "jobId": "b6e2b6a2-1e0a-4b8b-9c39-9a2f1f6a3b41",
    "name": "nightly",
    "kind": "Schedule",
    "enabled": true,
    "cronExpression": "0 2 * * *",
    "eventName": null,
    "nextRunAt": "2026-07-16T02:00:00+00:00",
    "lastFiredAt": null,
    "createdAt": "2026-07-15T12:00:00+00:00"
  }
]
```

### `GET /api/v1/schedules/{id}`

A single schedule, or `404`.

**Auth:** `triggers.manage`

**200 response:** same shape as one list item above.

### `POST /api/v1/projects/{projectId}/schedules`

Creates a schedule on a job that belongs to the project.

**Auth:** `triggers.manage`

**404** if the project or the referenced job doesn't exist. **400** if the job belongs to a *different*
project, `kind` isn't `"Schedule"`/`"Event"`, or (for a schedule) the cron expression is invalid.

**Request (cron):**

```json
{
  "jobId": "b6e2b6a2-1e0a-4b8b-9c39-9a2f1f6a3b41",
  "name": "nightly",
  "kind": "Schedule",
  "cronExpression": "0 2 * * *"
}
```

**Request (event):**

```json
{
  "jobId": "b6e2b6a2-1e0a-4b8b-9c39-9a2f1f6a3b41",
  "name": "on-deploy",
  "kind": "Event",
  "eventName": "deploy.finished"
}
```

**201 response:** the created schedule, `Location: /api/v1/schedules/{id}`.

### `PUT /api/v1/schedules/{id}`

Enables or pauses the schedule. **This is the only supported mutation on an existing schedule** — the
domain has no "rename"/"change cron" operation on a trigger, only enable/disable; to change the cron
expression, name, or event, delete and recreate. Re-enabling a cron schedule recomputes its next-run
time from now.

**Auth:** `triggers.manage`

**Request:**

```json
{ "enabled": false }
```

**200 response:** the updated schedule. **404** if it doesn't exist.

### `DELETE /api/v1/schedules/{id}`

Permanently removes the schedule.

**Auth:** `triggers.manage`

**204** on success, **404** if it didn't exist.

---

## Error shape

Every non-2xx response with a body uses:

```json
{ "error": "human-readable message" }
```

`404`/`401` responses may have an empty body (the status code alone is the signal).

## Not yet covered

- Reading job **run history** or artifacts (jobs.run / run results) — this contract is scoped to
  declarative *definitions* (what Terraform manages), not execution/observability. Use the portal or
  MCP for run history.
- Project **deletion** — see the note under Projects above.
- Changing a schedule's cron/name/event in place — see the note under `PUT /api/v1/schedules/{id}`.

---

## Entity data API (`/api/v1/{entity-name}`)

Auto-built from the workspace's registered **data entities** (portal: project → Entities). The
**project is not in the path** — `ProjectResolutionMiddleware` reads it from headers (or query):

```
X-Project-Id: 8f14e45f-ceea-4b3e-8c7a-6a5b6b6d8a10
# or
X-Project: storefront-api
```

(Query fallbacks: `?projectId=` / `?project=`.)

Authenticate with a **personal API token** from **Settings → API tokens** (prefix `pct_`), or the
workspace admin `PlaceContext:Api:Key`:

```
Authorization: Bearer pct_…
# or
X-Api-Key: pct_…
```

Personal tokens act as the minting user (same role + fine-grained permissions). Requires `data.read`.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/v1/entities` | Entity registry for the resolved project (name, slug, table, relations) |
| `GET` | `/api/v1/{entity-name}` | Paginated rows (`?page=&pageSize=&search=`) |
| `GET` | `/api/v1/{entity-name}/{key}` | Rows whose label column matches `key` |

`{entity-name}` matches the entity's display name, table name, or slug (e.g. `Sites` → `sites`).
Reserved segments used by the management API (`projects`, `jobs`, `schedules`, `entities`) never
resolve as entity names — and **project data will not let you create tables, views, or entities
with those names** (enforced in the store and on entity save).

Records are served from the project's isolated Postgres schema (same sandbox as the Data tab).

Example:

```bash
curl -H "Authorization: Bearer pct_…" \
     -H "X-Project: storefront-api" \
     https://ws.placecontext.ai/api/v1/sites
```
