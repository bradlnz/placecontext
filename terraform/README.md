# terraform-provider-placecontext

A Terraform provider for [PlaceContext](https://placecontext.ai) that manages **projects**,
**jobs**, and **schedules** declaratively through the workspace management REST API (`/api/v1`).
Built on the modern [terraform-plugin-framework](https://github.com/hashicorp/terraform-plugin-framework).

It targets the API documented in [`../docs/management-api.md`](../docs/management-api.md) — the field
names, status codes, and limitations below all come from that contract.

## Provider configuration

```hcl
terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

provider "placecontext" {
  endpoint = "https://acme.placecontext.ai" # or http://acme.localhost:7700 in dev
  api_key  = var.placecontext_api_key
}
```

| Argument   | Env var                  | Required | Notes |
|------------|--------------------------|----------|-------|
| `endpoint` | `PLACECONTEXT_ENDPOINT`  | yes (arg or env) | Workspace base URL. The `/api/v1` suffix is appended automatically — pass either the root or a URL that already ends in `/api/v1`. |
| `api_key`  | `PLACECONTEXT_API_KEY`   | yes (arg or env) | Workspace management API key. Marked **sensitive**; sent as `Authorization: Bearer <key>` and never logged. |

Either argument may be omitted from HCL and supplied via its environment variable instead. An
explicit argument wins over the environment variable.

### Obtaining / setting the API key

The management API is closed by default. The operator configures a single per-workspace admin key on
the **Host** as `PlaceContext:Api:Key` (an environment variable / app-config value on the server — it
is *not* something you create through the API). If no key is configured, every `/api/v1/*` request
returns `401`. A valid key authenticates as an `Owner`-equivalent caller scoped to the one workspace
resolved from the endpoint's subdomain.

Pass that same key to the provider as `api_key` / `PLACECONTEXT_API_KEY`.

## Resources

### `placecontext_project`

Registers a project (a codebase under watch). Registration is **idempotent by `path`**.

| Attribute | Type | | Description |
|---|---|---|---|
| `path` | string | required, force-new | Filesystem path of the codebase. |
| `name` | string | optional, computed | Display name; defaults to the last path segment. |
| `id` | string | computed | Server-assigned GUID. |
| `status` | string | computed | Lifecycle status (e.g. `Registered`). |
| `is_graphified` | bool | computed | Whether the knowledge graph is built. |

**Limitation — no delete.** The API exposes no `DELETE /projects/{id}`, so `terraform destroy` (or
removing the resource) only drops it from Terraform state and emits a warning; the project still
exists in the workspace. Changing `path` forces replacement (re-registration).

### `placecontext_job`

A generic map/reduce workload definition under a project. A **map step** is required; a **reduce
step** is optional. Each step is sourced from *either* a pre-built container image *or* inline code
run by a named runtime — never both (the API returns `400` otherwise).

| Attribute | Type | | Description |
|---|---|---|---|
| `project_id` | string | required, force-new | Owning project id. |
| `name` | string | required | Job name. |
| `description` | string | optional | Description. |
| `map_image` | string | optional | Pre-built image for the map step. |
| `map_runtime_id` | string | optional | Runtime for a code map step, e.g. `python`, `node`. |
| `map_source` | string | optional | Single-file inline source (legacy; prefer `map_files`). |
| `map_entrypoint` | string | optional | Entry filename within the work dir. |
| `map_files` | list(object(`path`,`content`)) | optional | Multi-file inline source; supersedes `map_source`. |
| `map_source_kind` | string | computed | Server-computed source kind (`image`/`code`). |
| `input_payloads` | list(string) | optional | One opaque input shard per entry. |
| `map_env` | map(string) | optional | Env vars for the map container. |
| `reduce_image` / `reduce_runtime_id` / `reduce_source` / `reduce_entrypoint` / `reduce_files` / `reduce_env` | same shapes as `map_*` | optional | Omit all for "no reduce step". |
| `reduce_source_kind` | string | computed | Source kind of the reduce step, or null. |
| `concurrency_limit` | number | optional, computed (default `1`) | Max concurrent map shards (≥ 1). |
| `success_exit_codes` | list(number) | optional, computed (default `[0]`) | Exit codes treated as success. |
| `partial_exit_codes` | list(number) | optional | Exit codes treated as partial success. |
| `allow_network_egress` | bool | optional, computed (default `false`) | When false, containers run `--network none`. |
| `parameters` | list(object(`name`,`label`,`required`,`type`,`options`)) | optional | Inputs prompted before a manual run. |
| `post_job_actions` | list(string) | optional | Any of `HtmlReport`, `Chart`, `Csv`, `RawBundle`, `HtmlOutput`. |
| `return_type` | string | optional, computed (default `Json`) | One of `Json`, `Table`, `Chart`, `Html`, `Csv`, `Text`, `Pdf`, `Image`, `Video`. |
| `return_file_name` | string | optional | Expected `/out` filename for `Pdf`/`Image`/`Video`. |
| `id`, `created_at`, `updated_at` | string | computed | Server-assigned. |

Full CRUD is supported (`POST`/`GET`/`PUT`/`DELETE`). Deleting a job does **not** cascade to its run
history, artifacts, or schedules — delete a job's schedules first for a clean removal.

`map_files` and `parameters` are nested **attributes**, so use list-of-objects assignment syntax
(`map_files = [ { path = "...", content = "..." } ]`), not block syntax.

### `placecontext_schedule`

A trigger on a job: a **cron schedule** (`kind = "Schedule"` + `cron_expression`) or an **event
subscription** (`kind = "Event"` + `event_name`).

| Attribute | Type | | Description |
|---|---|---|---|
| `project_id` | string | required, force-new | Owning project id. |
| `job_id` | string | required, force-new | Job the trigger fires. |
| `name` | string | required, force-new | Trigger name. |
| `kind` | string | required, force-new | `Schedule` or `Event`. |
| `cron_expression` | string | optional, force-new | Cron expr for a `Schedule`. |
| `event_name` | string | optional, force-new | Event name for an `Event`. |
| `enabled` | bool | optional, computed (default `true`) | Active state — the **only** in-place mutation. |
| `id`, `next_run_at`, `last_fired_at`, `created_at` | string | computed | Server-assigned. |

**Limitation — enable/disable only.** `PUT /schedules/{id}` accepts only `{ "enabled": ... }`. The
domain has no rename/change-cron operation, so every argument except `enabled` is marked
`RequiresReplace`: changing the cron expression, event, name, job, or kind deletes and recreates the
schedule. Re-enabling a cron schedule recomputes its next-run time.

## Importing

All three resources import by server id:

```sh
terraform import placecontext_project.storefront  <project-guid>
terraform import placecontext_job.nightly_report  <job-guid>
terraform import placecontext_schedule.nightly     <schedule-guid>
```

## Building & running against a local build

Terraform can't download this provider from a registry, so use CLI **dev overrides** to point at a
locally built binary.

```sh
# 1. Build the provider binary into this directory
cd terraform
go build -o terraform-provider-placecontext .

# 2. Tell the Terraform CLI to use it. Create ~/.terraformrc:
cat > ~/.terraformrc <<'EOF'
provider_installation {
  dev_overrides {
    "bradlnz/placecontext" = "/home/brad/code/devcontext/terraform"
  }
  # For all other providers, install as normal.
  direct {}
}
EOF

# 3. Run Terraform. With dev_overrides you SKIP `terraform init`.
cd examples
export PLACECONTEXT_ENDPOINT="http://acme.localhost:7700"
export PLACECONTEXT_API_KEY="<your key>"
terraform plan
terraform apply
```

> With `dev_overrides` in effect, `terraform init` is unnecessary (and Terraform prints a warning
> reminding you the override is active). Point the override at the **directory** containing the built
> `terraform-provider-placecontext` binary.

## Development

```sh
cd terraform
go build ./...   # compile provider + client
go vet ./...     # static checks
go test ./...    # unit tests (client via httptest, resource schema validation, model round-trips)
```

The client unit tests fake the API with `httptest.Server` (create/read/404/401/validation-error
paths); the provider tests assert each resource's `Schema` produces no diagnostics and that the
model↔DTO conversions round-trip nested blocks correctly. There are no acceptance tests here because
a live PlaceContext workspace isn't available in this environment.

## Layout

```
terraform/
├── main.go                     # providerserver.Serve entrypoint
├── internal/
│   ├── client/                 # typed HTTP client for /api/v1 + DTOs + tests
│   └── provider/               # provider + 3 resources + conversion helpers + tests
├── examples/main.tf            # provider config + one of each resource
└── README.md
```
