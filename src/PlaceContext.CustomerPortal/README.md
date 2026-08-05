# Customer portal service

This is the separately deployable external customer-portal host. It is deliberately not the
Placecontext operator portal and binds to exactly one tenant and one public hostname per deployment.

Required configuration:

- `CustomerPortal__TenantId`: an existing tenant UUID
- `CustomerPortal__Domain`: the exact hostname routed to this deployment
- `PlaceContext__ConnectionString`: the tenant database connection string

The Kubernetes template is `deploy/k3s/customer-portal-tenant.yaml`. It creates a dedicated namespace,
service account, deployment, service, and TLS ingress. Customer authentication and the restricted CRM
API are the next layer behind this boundary; the current service intentionally exposes only health and
tenant-context bootstrap endpoints.
