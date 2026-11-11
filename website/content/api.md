# Integrate through APIs and Terraform

Use the web portal for interactive work, MCP for AI clients, and the management API or Terraform for repeatable provisioning and service integrations.

## Select the workspace origin

API requests go to the workspace's own origin. Tenant resolution is based on that host; a query parameter does not select an arbitrary tenant.

The management API is rooted at `/api/v1`. JSON uses camelCase and timestamps use RFC 3339.

## Use the right credential

Endpoints opt into specific authentication schemes. Workspace API keys and personal API tokens have different authority and supported routes. A workspace key is admin-equivalent; a personal token inherits its member's permissions.

Consult the [management API reference](https://github.com/bradlnz/placecontext/blob/main/docs/management-api.md) for each route's accepted scheme. Keep keys server-side and avoid embedding them in source or a website.

## Read project data

Authorized personal-token integrations can use the project data endpoints:

```http
GET /api/v1/projects/{projectId}/data/tables
POST /api/v1/projects/{projectId}/data/query
```

The query body is a JSON object:

```json
{ "sql": "SELECT count(*) AS total FROM orders" }
```

Queries run read-only with result limits. Check the returned truncation flag before relying on a result. For jobs, prefer the transient, run-scoped project data credential over a long-lived personal token.

## Submit durable work

Managed workflow triggers return a queued receipt and polling link. MCP integrations can also call `submit_job_chain` and poll `get_job_chain_submission`. A submitted receipt acknowledges durable intake; it does not mean execution succeeded.

Use idempotency keys where the endpoint supports them and inspect terminal run output before reporting success.

## Provision with Terraform

The repository includes a Terraform provider and examples for projects, jobs, and schedules. Use the [Terraform guide](https://github.com/bradlnz/placecontext/blob/main/terraform/README.md) for the supported resources and authentication configuration.

## Inspect failures

Expect `401` for missing or invalid authentication, `403` for insufficient permission, `404` for unavailable resources, and `409` for conflicts. Managed asynchronous submissions can return `202`; poll the receipt rather than retrying the execution blindly.
