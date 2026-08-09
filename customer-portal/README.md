# Placecontext customer portal

This Rails application is the external, tenant-specific customer portal. It is
deployed as the same image into a dedicated Kubernetes namespace for each
tenant and receives its identity from the host configuration:

- `PLACE_CONTEXT_TENANT_ID`
- `PLACE_CONTEXT_TENANT_SLUG`
- `PLACE_CONTEXT_PORTAL_DOMAIN`
- `PLACE_CONTEXT_PORTAL_SHARED_HOST`
- `PLACE_CONTEXT_PORTAL_PATH`
- `RAILS_RELATIVE_URL_ROOT`
- `DATABASE_URL`
- `PLACE_CONTEXT_CRM_API_URL`
- `PLACE_CONTEXT_CUSTOMER_PORTAL_API_KEY`
- `PLACE_CONTEXT_PROVISIONING_KEY`
- `SMTP_ADDRESS`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`
- `PORTAL_DEFAULT_USER_NAME`
- `PORTAL_DEFAULT_USER_EMAIL`
- `PORTAL_DEFAULT_USER_PASSWORD` (optional)

Devise owns portal authentication in the local `portal_users` table. These are
not Placecontext operator users. Placecontext enables, disables, and invites
these accounts from its Users section. CRM data remains owned by Placecontext
and is accessed through a narrow service-to-service API; this app must not
connect to the core database.

The deployment supports a tenant custom/subdomain origin and the shared path
origin `/p/:customer_name`. Path deployments set `RAILS_RELATIVE_URL_ROOT` so
generated links remain inside the tenant prefix.
