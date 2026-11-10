# Terraform provider for PlaceContext

Manage PlaceContext **projects, jobs and schedules** as code through the
workspace management API (`/api/v1`). This is the *platform* provider — it
declares what runs inside a workspace. The infrastructure that hosts the control
plane and workers lives under [`deploy/`](../deploy) instead.

```hcl
terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

provider "placecontext" {
  # base_url = "https://acme.placecontext.io"
}

resource "placecontext_project" "revenue" {
  path = "data/revenue"
  name = "Revenue"
}

resource "placecontext_schedule" "nightly" {
  project_id      = placecontext_project.revenue.id
  job_id          = placecontext_job.rollup.id
  name            = "Nightly rollup"
  kind            = "Schedule"
  cron_expression = "0 2 * * *"
}
```

## Requirements

| Tool | Version |
| ---- | ------- |
| Terraform | 1.11 or newer |
| Go (only to build the provider) | 1.27 or newer |
| PlaceContext | a workspace with the management API enabled |

## Authentication

The management API authenticates with a single workspace-scoped key configured
on the host as `PlaceContext:Api:Key`. The key is sent as
`Authorization: Bearer <key>` (the API also accepts `X-Api-Key`). Configure it
with `PLACECONTEXT_API_KEY` or the provider's `api_key` argument; marking it
sensitive keeps it out of plan output.

The key is scoped to the workspace its host resolves: point `base_url` at that
workspace's origin.

```bash
export PLACECONTEXT_URL="https://acme.placecontext.io"
export PLACECONTEXT_API_KEY="$(cat ~/.config/placecontext/api-key)"
```

For a single-tenant self-hosted install the origin is usually
`http://localhost:7700`. See [`docs/management-api.md`](../docs/management-api.md)
for the underlying endpoints, permissions and JSON schemas.

## Install

The provider is not yet in the public registry. Build it and point Terraform at
the binary with a development override:

```bash
cd terraform
go build -o bin/terraform-provider-placecontext .
```

`~/.terraformrc`:

```hcl
provider_installation {
  dev_overrides {
    "bradlnz/placecontext" = "/absolute/path/to/terraform/bin"
  }
  direct {}
}
```

Terraform prints a "provider development overrides are in effect" warning in
this mode; that is expected. Omit `terraform init` while the override is active.

## Provider configuration

| Argument | Environment | Description |
| -------- | ----------- | ----------- |
| `base_url` | `PLACECONTEXT_URL` | Workspace origin, e.g. `https://acme.placecontext.io`. |
| `api_key` | `PLACECONTEXT_API_KEY` | Management API key (sensitive). |

## Resources

| Resource | Operations | Notes |
| -------- | ---------- | ----- |
| `placecontext_project` | create, read | Idempotent by `path`. No rename or delete endpoint: `path` forces replacement, and `destroy` only removes the project from Terraform state (with a warning). Changing `name` after creation fails with a clear diagnostic. |
| `placecontext_job` | create, read, update, delete | Full map/reduce definition. `project_id` forces replacement. Supply an image *or* a runtime plus source/files for each step. |
| `placecontext_schedule` | create, read, update, delete | Cron (`kind = "Schedule"`, `cron_expression`) or event (`kind = "Event"`, `event_name`) trigger. `project_id`, `job_id` and `kind` force replacement. |

Every resource supports `terraform import` by UUID:

```bash
terraform import placecontext_job.rollup 11111111-1111-1111-1111-111111111111
```

### Defaults and drift

The API applies documented defaults that the provider surfaces as computed
values, so a minimal configuration stays small:

- `concurrency_limit` → `4`
- `return_type` → `"Json"`
- `success_exit_codes` → `[0]`
- `allow_network_egress` / `allow_api_invocation` → `false`
- `retry_count` / `retry_delay_seconds` → `0`
- `enabled` (schedules) → `true`

Attributes with a server default are `optional + computed`: omit them to accept
the default, or set them to pin a value.

## Data sources

| Data source | Lookup | Returns |
| ----------- | ------ | ------- |
| `placecontext_project` | `id` **or** `path` | name, status, `is_graphified` |
| `placecontext_job` | `id` | the full job definition |

```hcl
data "placecontext_project" "customers" {
  path = "data/customers"
}

output "customers_id" {
  value = data.placecontext_project.customers.id
}
```

Data sources without a managed dependency are read during `plan`. If you look up
a project that the same configuration creates, add
`depends_on = [placecontext_project.example]` so the read happens after it exists.

## Examples

- [`examples/provider`](examples/provider/provider.tf) — provider block
- [`examples/project`](examples/project/main.tf) — managed project and lookup
- [`examples/job`](examples/job/main.tf) — image and code-based jobs
- [`examples/schedule`](examples/schedule/main.tf) — cron and event triggers

## Development

```bash
cd terraform
go build ./...
go vet ./...
go test ./...
gofmt -l .
```

The tests use `httptest` to verify request shapes, authentication headers, JSON
casing and the 404 handling the provider relies on. They do not need a running
workspace.
