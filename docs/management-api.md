# PlaceContext management API (`/api/v1`)

The management API is the machine-facing contract for automation and
infrastructure-as-code. It is what the [Terraform provider](../terraform/README.md)
declares projects, jobs and schedules against, and it is stable independently of
the portal's internal commands and view models.

This document covers the parts of `/api/v1` that are relevant to provisioning
(projects, jobs, schedules) in full, then summarises the data, entity, search and
queue surfaces.

## Base URL and workspace scoping

| Deployment | Base URL |
| ---------- | -------- |
| Managed / multi-tenant | `https://<workspace>.placecontext.io` |
| Self-hosted, single tenant | the control-plane origin, e.g. `http://localhost:7700` |

`TenantResolutionMiddleware` resolves the workspace from the request host
(subdomain) before authentication. Every repository call is then filtered to that
tenant by EF's global query filter. Point clients at the workspace's own origin;
there is no `?tenant=` parameter.

JSON is camelCase (`mapImage`, `isGraphified`, `concurrencyLimit`). Timestamps
are RFC 3339 strings. Request bodies are `application/json`.

## Authentication

Two independent schemes exist. A request can only use the scheme(s) an endpoint
opts into.

### Workspace API key (`ApiKey`)

Used by **projects, jobs and schedules**. Configured on the host as
`PlaceContext:Api:Key` and sent as either:

```http
Authorization: Bearer <key>
```

or

```http
X-Api-Key: <key>
```

- Deny-by-default: if no key is configured on the host, every call returns `401`.
- The presented key is never logged.
- A successful match establishes an admin-equivalent principal (role `Owner`), so
  it holds the full permission catalog by default.

### Personal API token (`UserApiToken`)

Used by the data, entity, search and queue APIs. Minted from **Settings → API
tokens**, prefixed `pct_`, and sent the same two ways. The token acts as the
owning member, so their role and permission overrides apply.

Data/entity/search calls also need a project, supplied with `X-Project-Id: <guid>`
or `X-Project: <name>` (Search and Entities accept either the API key or a
personal token).

A failed or missing credential returns `401`; a valid credential without the
required permission returns `403`.

## Conventions

| Status | Meaning |
| ------ | ------- |
| `200 OK` | Read or update succeeded. |
| `201 Created` | Resource created; `Location` points at the new resource. |
| `202 Accepted` | Managed-hosting job/chain submission queued; poll the receipt. |
| `204 No Content` | Delete succeeded. |
| `400 Bad Request` | Invalid body, workload spec, cron expression or query. |
| `401 Unauthorized` | Missing/invalid credential. |
| `403 Forbidden` | Authenticated but missing the permission, or API invocation is disabled on the job. |
| `404 Not Found` | The resource, project or parent job does not exist. |
| `409 Conflict` | The request conflicts with existing state (e.g. a duplicate submission). |
| `503 Service Unavailable` | Managed workflow queue is not configured. |

Errors use a small JSON envelope:

```json
{ "error": "human-readable reason" }
```

Projects and jobs accept an `Idempotency-Key` header where noted. Retrying a
create is safe: project creation is idempotent by `path` and returns `201` with
the existing project.

### Permissions used by this document

`projects.view`, `projects.manage`, `jobs.view`, `jobs.edit`, `jobs.run`,
`jobs.manage`, `triggers.manage`, `data.read`. The full catalog lives in
`PlaceContext.Application.Access.Models.Permission`.

## Projects

Projects have no delete path in the domain (only an internal archive action), so
there is deliberately **no `DELETE`** endpoint.

### `GET /api/v1/projects`

Returns every project in the workspace.

```json
[
  {
    "id": "0f3c2a9e-5b7c-4b1e-9f0a-2a6d8e4c1b33",
    "name": "Revenue",
    "path": "data/revenue",
    "status": "Registered",
    "isGraphified": false
  }
]
```

Permission: `projects.view`.

### `GET /api/v1/projects/{id}`

Returns one project, or `404`. Permission: `projects.view`.

### `POST /api/v1/projects`

Registers a project. **Idempotent by `path`**: re-posting a known path returns the
existing project with `201` and does not change it. The display name is fixed at
creation; the name argument is ignored for an existing path.

```json
{ "path": "data/revenue", "name": "Revenue" }
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `path` | string | yes | Project path; unique within the workspace. |
| `name` | string | no | Defaults to the last path segment. |

Permission: `projects.manage`. Response: the `ProjectResponse` above.

## Jobs

### `GET /api/v1/projects/{projectId}/jobs`

All jobs under a project, or `404` if the project does not exist.
Permission: `jobs.view`.

### `GET /api/v1/jobs/{id}`

One job definition, or `404`. Permission: `jobs.view`.

### `POST /api/v1/projects/{projectId}/jobs`

Creates a job under a project. `404` if the project does not exist; `400` for an
invalid workload spec (e.g. neither an image nor a code source, or both).
Permission: `jobs.edit`.

### `PUT /api/v1/jobs/{id}`

Replaces the job's configuration with the full desired state. `404` if the job
does not exist; `400` on an invalid workload spec. Permission: `jobs.edit`.

### `DELETE /api/v1/jobs/{id}`

Permanently removes a job definition (`204`). Does not cascade to run history,
artifacts, data mappings or triggers — delete or archive those first if a clean
slate is required. `404` if it did not exist. Permission: `jobs.edit`.

### Job request body

Exactly one map workload is required: either `mapImage`, or a code workload
(`mapRuntimeId` plus `mapSource` and/or `mapFiles`). The optional reduce step
follows the same rule; omit every `reduce*` field for a map-only job.

| Field | Type | Default | Notes |
| ----- | ---- | ------- | ----- |
| `name` | string | — | Required. |
| `description` | string? | null | |
| `mapImage` | string? | null | Container image for the map step. |
| `mapRuntimeId` | string? | null | Runtime for a code-based map step. |
| `mapSource` | string? | null | Entrypoint source for a code-based map step. |
| `mapEntrypoint` | string? | null | Entrypoint file/command. |
| `mapFiles` | `[{path, content}]` | null | Source files for a code-based map step. |
| `inputPayloads` | string[] | null | Default shard payloads. |
| `mapEnv` | `{string: string}` | null | Map-step environment. |
| `reduceImage` | string? | null | Image for the reduce step. |
| `reduceRuntimeId` | string? | null | Runtime for a code-based reduce step. |
| `reduceSource` | string? | null | Entrypoint source for a code-based reduce step. |
| `reduceEntrypoint` | string? | null | Reduce entrypoint. |
| `reduceFiles` | `[{path, content}]` | null | Source files for a code-based reduce step. |
| `reduceEnv` | `{string: string}` | null | Reduce-step environment. |
| `concurrencyLimit` | integer | `4` | Maximum concurrent map shards. |
| `successExitCodes` | integer[] | `[0]` | |
| `partialExitCodes` | integer[] | `[]` | |
| `allowNetworkEgress` | boolean | `false` | Outbound network access (`--network none` when false). |
| `allowApiInvocation` | boolean | `false` | Whether the job may call the API by name. |
| `parameters` | `[{name, label?, required?, type?, options?}]` | null | Declared runtime inputs. `type` is `text`, `number`, `date`, `datetime-local`, `time`, `select`, `checkbox` or `file` (default `text`); `required` defaults to `true`. |
| `postJobActions` | string[] | null | `HtmlReport`, `Chart`, `Csv`, `RawBundle`, `HtmlOutput`. |
| `returnType` | string | `"Json"` | `Json`, `Table`, `Chart`, `Html`, `Csv`, `Text`, `Pdf`, `Image`, `Video`. |
| `returnFileName` | string? | null | Expected `/out` file for `Pdf`/`Image`/`Video`. |
| `retryCount` | integer | `0` | Automatic retries on failure. |
| `retryDelaySeconds` | integer | `0` | Fixed delay between retries. |
| `mcpConnectionIds` | uuid[] | null | MCP connections injected as env vars at runtime. |

Example:

```json
{
  "name": "Nightly revenue rollup",
  "description": "Summarises yesterday's orders.",
  "mapImage": "ghcr.io/acme/revenue:1.4.0",
  "mapEnv": { "TZ": "UTC" },
  "inputPayloads": ["2026-01-01"],
  "concurrencyLimit": 2,
  "successExitCodes": [0],
  "partialExitCodes": [2],
  "allowNetworkEgress": false,
  "parameters": [
    { "name": "report_date", "label": "Report date", "type": "date", "required": true }
  ],
  "returnType": "Table",
  "postJobActions": ["Csv"]
}
```

### Job response

The response echoes the desired state plus server-resolved fields:

```json
{
  "id": "6f1d…",
  "projectId": "0f3c…",
  "name": "Nightly revenue rollup",
  "description": "Summarises yesterday's orders.",
  "mapSourceKind": "image",
  "mapImage": "ghcr.io/acme/revenue:1.4.0",
  "mapRuntimeId": null,
  "mapSource": null,
  "mapEntrypoint": null,
  "mapFiles": [],
  "inputPayloads": ["2026-01-01"],
  "mapEnv": { "TZ": "UTC" },
  "reduceSourceKind": null,
  "reduceImage": null,
  "reduceRuntimeId": null,
  "reduceSource": null,
  "reduceEntrypoint": null,
  "reduceFiles": [],
  "reduceEnv": null,
  "concurrencyLimit": 2,
  "successExitCodes": [0],
  "partialExitCodes": [2],
  "allowNetworkEgress": false,
  "allowApiInvocation": false,
  "parameters": [{ "name": "report_date", "label": "Report date", "required": true, "type": "date", "options": null }],
  "postJobActions": ["Csv"],
  "returnType": "Table",
  "returnFileName": null,
  "retryCount": 0,
  "retryDelaySeconds": 0,
  "mcpConnectionIds": [],
  "createdAt": "2026-05-01T09:12:44.123+00:00",
  "updatedAt": "2026-05-01T09:12:44.123+00:00"
}
```

`mapSourceKind` / `reduceSourceKind` are `image` or `code`. `reduceSourceKind` is
null when the job has no reduce step.

### Job runs and workflows

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `POST` | `/api/v1/job-runs/{runId}/cancel` | `jobs.manage` | Cancel a run; idempotent. Returns `true`/`false`. |
| `POST` | `/api/v1/workflow-runs/{chainRunId}/cancel` (alias `/chain-runs/…`) | `jobs.manage` | Cancel a chain run and its running steps. |
| `POST` | `/api/v1/workflows/{chainId}/trigger` (alias `/chains/…`) | `jobs.edit` | Start a chain. Body `{ "inputPayload": "…" }`; optional `Idempotency-Key`. Managed hosting returns `202` + submission receipt; self-host returns the run view. |
| `GET` | `/api/v1/workflow-submissions/{trackingId}` | `jobs.view` | Poll a managed submission receipt. |
| `POST` | `/api/v1/workflows/{chainId}/replay` (alias `/chains/…`) | `jobs.edit` | Replay from a failed step. Body `{ originalRunId, fromStepIndex?, inputPayload?, stepPayloadOverrides? }`. |

## Schedules

Schedules cover both cron triggers (`kind = "Schedule"`) and event subscriptions
(`kind = "Event"`). Every endpoint requires `triggers.manage` — the catalog has no
separate read permission.

### `GET /api/v1/projects/{projectId}/schedules`

All triggers under a project, or `404` if the project does not exist.

### `GET /api/v1/schedules/{id}`

One trigger, or `404`.

### `POST /api/v1/projects/{projectId}/schedules`

Creates a trigger on a job. `404` if the project or referenced job does not
exist; `400` if the job belongs to a different project, the kind is unrecognized,
or the cron expression is invalid.

```json
{
  "jobId": "11111111-1111-1111-1111-111111111111",
  "name": "Nightly rollup",
  "kind": "Schedule",
  "cronExpression": "0 2 * * *",
  "eventName": null
}
```

| Field | Type | Required | Notes |
| ----- | ---- | -------- | ----- |
| `jobId` | uuid | yes | Job the trigger runs; must belong to `projectId`. |
| `name` | string | yes | |
| `kind` | string | yes | `Schedule` or `Event`. |
| `cronExpression` | string? | when `Schedule` | Standard five-field cron. |
| `eventName` | string? | when `Event` | Event to subscribe to. |

New triggers are enabled.

### `PUT /api/v1/schedules/{id}`

Full desired state; `null` fields are left unchanged. Re-enabling a cron schedule
or changing its expression recomputes `nextRunAt`. `404` if the trigger is gone.

```json
{ "name": "Nightly rollup", "cronExpression": "0 3 * * *", "enabled": false }
```

### `DELETE /api/v1/schedules/{id}`

Removes the trigger (`204`), or `404` if it did not exist.

### Schedule response

```json
{
  "id": "9a7e…",
  "projectId": "0f3c…",
  "jobId": "6f1d…",
  "name": "Nightly rollup",
  "kind": "Schedule",
  "enabled": true,
  "cronExpression": "0 2 * * *",
  "eventName": null,
  "nextRunAt": "2026-05-02T02:00:00+00:00",
  "lastFiredAt": "2026-05-01T02:00:00+00:00",
  "createdAt": "2026-04-30T16:20:11.456+00:00"
}
```

## Data, entities, search and queues

These surfaces use personal API tokens (`pct_…`) and, for project-scoped calls,
`X-Project-Id` / `X-Project`.

### Project data

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `GET` | `/api/v1/projects/{projectId}/data/tables` | `data.read` | List tables and views. |
| `POST` | `/api/v1/projects/{projectId}/data/query` | `data.read` | Read-only SQL. Body `{ "sql": "select …" }` (1–32768 chars). Returns `{ columns, rows, truncated }`. |

### Entities

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `GET` | `/api/v1/entities` | `data.read` | Entity registry for the resolved project. |
| `GET` | `/api/v1/{entityName}?search=&page=&pageSize=` | `data.read` | Paged rows (`pageSize` default 50). |
| `GET` | `/api/v1/{entityName}/{key}` | `data.read` | Rows whose label/first column equals `key`. |
| `POST` | `/api/v1/{entityName}/jobs/{jobId}/run` | `jobs.run` | Run a job for the project. Body `{ "inputPayload": "…", "runId": "…"? }`. |
| `POST` | `/api/v1/{jobName}` | `jobs.run` | Run a job by name; requires `allowApiInvocation = true` on the job. |

### Search

`GET /api/v1/search?q=<term>&limit=25` — permission `data.read`. `q` is 2–200
characters; `limit` is 1–100.

### Sources and feeds

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `GET` | `/api/v1/projects/{projectId}/sources` | `data.read` | List data sources. |
| `POST` | `/api/v1/projects/{projectId}/sources` | `secrets.manage` | Register a source (`name`, `connectionString`, `schema`, `table`, `keyColumn`, `columns`, `tags`, optional `processing`, `queue`). |
| `PUT` | `/api/v1/projects/{projectId}/sources/{sourceId}/processing` | `secrets.manage` | Configure processing. |
| `PUT` | `/api/v1/projects/{projectId}/sources/{sourceId}/queue` | `secrets.manage` | Configure the queue. |
| `POST` | `/api/v1/projects/{projectId}/sources/{sourceId}/sync` | `data.write` | Sync source records. |
| `GET` | `/api/v1/projects/{projectId}/sources/{sourceId}/records` | `data.read` | Read source records. |
| `POST` | `/api/v1/projects/{projectId}/sources/search` | `data.read` | Search across sources. |
| `DELETE` | `/api/v1/projects/{projectId}/sources/{sourceId}` | `secrets.manage` | Remove a source. |
| `POST` | `/api/v1/projects/{projectId}/sources/{sourceId}/inbound` | source config | Inbound webhook payload. |
| `POST` | `/api/v1/projects/{projectId}/sources/{sourceId}/inbound/email` | source config | Inbound email payload. |

### Job data

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `POST` | `/api/v1/job-data/sync-source` | `data.write` | Sync a source from a job. |
| `GET` | `/api/v1/job-data/sources/{sourceId}/records` | `data.read` | Read source records. |
| `POST` | `/api/v1/job-data/source-records` | `data.write` | Write source records. |
| `GET` | `/api/v1/job-data/tables` | `data.read` | List job-data tables. |
| `POST` | `/api/v1/job-data/query` | `data.read` | Query job data. |

### Queues

| Method | Path | Permission | Purpose |
| ------ | ---- | ---------- | ------- |
| `POST` | `/api/v1/queues/{queueId}/messages` | `jobs.run` | Submit a message. Body is a JSON payload; optional `Idempotency-Key` and `X-Execute-At`. Returns `202` with a receipt. |
| `GET` | `/api/v1/queues/{queueId}/messages/{trackingId}` | `jobs.view` | Poll a message receipt. |

## Terraform mapping

| Terraform | Endpoint(s) |
| --------- | ----------- |
| `placecontext_project` | `POST /api/v1/projects`, `GET /api/v1/projects/{id}` |
| `placecontext_job` | `POST /api/v1/projects/{projectId}/jobs`, `GET`/`PUT`/`DELETE /api/v1/jobs/{id}` |
| `placecontext_schedule` | `POST /api/v1/projects/{projectId}/schedules`, `GET`/`PUT`/`DELETE /api/v1/schedules/{id}` |

The provider's `base_url` is the workspace origin and `api_key` is
`PlaceContext:Api:Key`. See [`terraform/README.md`](../terraform/README.md) for
resource arguments and lifecycle notes.

Workflow schedules accept `workflowId` with `jobId: null` at `POST /api/v1/projects/{projectId}/schedules`, for example `{"workflowId":"<workflow UUID>","jobId":null,"name":"Daily check","kind":"Schedule","cronExpression":"0 9 * * *","eventName":null}`. The workflow must belong to the route's project. The schedule runs as its creator using current permissions. Responses include `workflowId`; legacy job targets retain `jobId`.
