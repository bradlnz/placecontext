# PlaceContext Provider

Manage PlaceContext **projects**, **jobs**, and **schedules** declaratively through the workspace
management REST API (`/api/v1`). See the top-level [`../README.md`](../README.md) for full argument
and attribute reference, dev-override setup, and API limitations.

## Example Usage

```hcl
provider "placecontext" {
  endpoint = "https://acme.placecontext.ai" # or PLACECONTEXT_ENDPOINT
  api_key  = var.placecontext_api_key       # or PLACECONTEXT_API_KEY (sensitive)
}
```

## Schema

### Optional

- `endpoint` (String) — Workspace base URL; `/api/v1` is appended automatically. Env: `PLACECONTEXT_ENDPOINT`.
- `api_key` (String, Sensitive) — Workspace management API key (`PlaceContext:Api:Key`). Env: `PLACECONTEXT_API_KEY`.

## Resources

- `placecontext_project` — register a codebase (idempotent by path; no remote delete).
- `placecontext_job` — a map/reduce workload definition (image or code source; full CRUD).
- `placecontext_schedule` — a cron or event trigger on a job (only enable/disable is mutable in place).
