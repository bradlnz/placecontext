# Job and Data platform — SaaS implementation foundation

PlaceContext's differentiation should be **connected data + isolated compute jobs + durable
workflows + verified outputs + human control**, not a separate Agent Store or a collection
of generic dashboard products.

## Implemented in this change

### Data contract job

Under **Jobs → New job → Data quality**, choose **Validate data contract**. The template
contains a dependency-free Python step that reads a JSON array or a `{"records":[...]}`
payload from stdin. Place it **between ingestion and any write/delivery action**. Validation
runs under normal job sandbox limits, without egress or provider credentials.

Job environment options (owned by the workflow author, not the incoming payload):

| Setting | Example | Contract |
|---|---|---|
| `MIN_ROWS` | `1` | Minimum number of records, 0–5,000 |
| `REQUIRED_FIELDS` | `id,email` | Dotted JSON paths that must have a nonnull/nonempty value |
| `UNIQUE_FIELD` | `id` | Check uniqueness and presence of a stable key |
| `MAX_NULL_RATE` | `0.1` | Maximum proportion of missing values in each required field (0–1) |
| `FIELD_TYPES_JSON` | `{"id":"string","value":"number"}` | Optional types: string, number, boolean, object, array |

The template limits input to 1 MiB and 5,000 records per batch; partition larger datasets.
On success it exits 0 and outputs the **original records** with a `quality` evidence
summary. On failure it emits a **quality-only** JSON failure receipt and exits 1.
The normal workflow engine then stops before downstream stages. There is no AI judgment
inside the validator. Values never appear in failure messages; only field names/counts
and row numbers are reported.

This is a **batch/schema/null/uniqueness** check, not a full database schema registry,
freshness SLA, distributed data quality service or connector token validator.

### Workflow readiness and safer edits

The existing Workflows editor has **Pre-run checks**, with the same read-only checks
available to the Build assistant via `check_workflow_readiness`. It examines **existing
job definitions only**: unresolved references, missing code/image/runtime, required
parameters, external actions without a preceding human gate on the inspected path,
network permissions and declared MCP connections. It does **not** execute a job,
contact a data source, infer real data quality or claim that credentials are valid.

The interactive editor retains the workflow revision initially opened. Updating a
workflow with a stale revision returns a conflict instead of silently overwriting
other changes. At the persistence layer, the original timestamp is used as an EF
optimistic concurrency token; racing commits cannot silently replace one another.
The existing `UpdatedAt` column is reused; no schema change is required. This is
an edit-conflict guard, **not** immutable workflow releases or job-code pinning.

### Validation

- `tests/deploy/test_data_quality_template.py` extracts the **exact embedded Python**
  source and exercises passing, duplicate, missing, type violation, policy manipulation,
  malformed configuration and empty input.
- Host tests validate the catalogue entry and editor/Build tool affordances.
- Application tests validate readiness behaviour and stale edit rejection.
- `AppDbContextModelSnapshot` is updated to match the concurrency configuration.

The application build, integration tests and browser tests still require a .NET-enabled
runner. Do not ship without green CI and a PostgreSQL concurrency integration test.

## SaaS work still needed

This is a foundation, not a complete SaaS launch. Priorities:

1. **Managed, reusable connections:** OAuth2 connection records, provider-specific scopes,
   refresh-token rotation, connection tests, connection ownership and revocation. Start
   with a narrow set relevant to the first paying data/job workflow customer.
2. **Immutable releases:** publish a workflow revision with hashes/pinned job source,
   explicit versions of datasets and connectors, preview/test runs, environment promotion
   and rollback. A timestamp-based editor conflict guard is not enough.
3. **Billing and entitlements:** hosted workspace signup, trial state, signed Stripe webhooks,
   nonnegative usage ledger and per-tenant plan limits enforced by API, queue, workers and
   compute resources. Existing token usage and Kubernetes quotas alone are not billing.
4. **Data asset contracts:** persisted schema, lineage, freshness/SLA checks and quality
   history tied to actual data outputs and workflow runs.
5. **Enterprise operations:** approval assignments, escalation policies, audit export,
   incident alerts, support workflows, tenant-level retention, backup/restore guarantees,
   federated SSO/SCIM and compliance evidence.

## Why this angle matters

Dagster's asset checks show why data quality should be attached to execution and may
block downstream work: https://docs.dagster.io/ . Kestra combines code jobs, scheduled
and event-triggered pipelines, data handling and approval steps:
https://kestra.io/docs .

PlaceContext can differentiate by combining **general-purpose secure job compute**
with **project-owned SQL/data tables**, **MCP/API connections**, **human decisions**
and **durable workflow execution**. The unit of product value is a tested,
observable job-to-data outcome, not an AI agent or a stand-alone dashboard.
