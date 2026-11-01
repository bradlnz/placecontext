# SaaS run plans and managed Microsoft OAuth (first integration)

This change is built on the workflow-first/job-data foundation. It does **not** create a
separate Agent Store or data-profile product. Jobs and Workflows remain the primary
execution surface; plan management lives under Settings → Billing, and Microsoft 365
authorisation lives under Settings → Connections.

## Plan entitlements and run accounting

One **top-level workflow start** uses one included run, however many child jobs execute.
One **standalone job start** also uses one run. Automatic retries and durable workflow
continuations reuse their original execution's key, and do not use another run.
A manual replay is a new top-level execution and consumes one run.

The included allowances are currently code-configured defaults, **not confirmed prices**:

| Plan | Included runs per billing period |
| --- | ---: |
| Free | 100 per calendar month |
| Starter | 1,000 |
| Growth | 10,000 |
| Business | 50,000 |

The authenticated workspace's paid billing period comes from its verified Stripe
subscription item, not from client time or a query parameter. Free plans reset on
the UTC calendar month. Reserves are written atomically under a PostgreSQL row lock
in `tenant_run_plans`. The immutable `billable_runs` key
(`workflow:<run GUID>` / `job:<root GUID>`) prevents duplicate charging
on retries, same-run submissions and concurrent workers.

Run reservations happen immediately **before** a new execution starts.
Failed runs consume an allowance; background/scheduled submissions may be accepted
into a queue but cannot start when the allowance is exhausted. A suspended run
can always be resumed without another charge. Reads, stored data, existing run
history and billing management remain available when runs are exhausted.

For existing tenants without a plan record, the first execution creates a
100-run Free entitlement. This is a deliberate initial default; change the
Free allocation and product onboarding policy before public release.

### Prevent free-trial tenant abuse on hosted SaaS

Enable `PlaceContext__SaaS__RequireExplicitTenantProvisioning=true` on a hosted deployment.
With this flag, visiting an unknown subdomain returns 404 instead of creating
another tenant automatically. Provision the initial default tenant and customer
tenants through an authenticated administrative onboarding process before
enabling the flag. The current change does **not** implement public self-service
tenant signup, email-domain verification or payment-gated trial creation;
do not expose unbounded subdomain auto-provisioning when run plans are enabled.
Self-hosted/lab installations can retain the previous auto-provisioning behaviour
with the flag disabled.

## Stripe configuration

Set these secrets server-side. Never expose them through the browser or job env:

```
PlaceContext__Stripe__SecretKey=sk_live_...
PlaceContext__Stripe__WebhookSecret=whsec_...
PlaceContext__Stripe__StarterPriceId=price_...
PlaceContext__Stripe__GrowthPriceId=price_...
PlaceContext__Stripe__BusinessPriceId=price_...
```

Create Stripe **fixed monthly subscription** prices for each plan, set the
corresponding Price IDs, and configure the webhook endpoint at
`https://<your-service>/api/billing/stripe/webhook`.

Subscribe to:
- `checkout.session.completed` (links verified customer identity; does not grant paid runs);
- `customer.subscription.created`;
- `customer.subscription.updated`;
- `customer.subscription.deleted`.

Only the HMAC-SHA256-signed Stripe events can update a paid run entitlement.
Webhook signatures have a five-minute freshness window, event IDs are persisted
to deduplicate deliveries, and older subscription timestamps cannot roll back
newer entitlement periods. The server validates the price against its configured
plan IDs, tenant linkage and the subscription billing period. Active/trialing
subscriptions can start new runs; past-due, unpaid and cancelled subscriptions
are blocked pending a valid subscription update.

Checkout and billing portal are hosted by Stripe. Billing status is never
changed because of the checkout success-return URL.

**Production hardening still required:** race/load tests against a real Postgres
database, authenticated billing-portal/checkout end-to-end tests in Stripe test
mode, webhook delivery ordering tests, and accurate plan pricing/cancellation
policies. This is a guarded implementation, not a completed billing launch.

## Microsoft 365 managed OAuth

Register a Microsoft Entra **Web** application and exact redirect URI:

```
https://<workspace-origin>/api/managed-oauth/microsoft/callback
```

Configure:
```
PlaceContext__ManagedOAuth__Microsoft__ClientId=<application-id>
PlaceContext__ManagedOAuth__Microsoft__ClientSecret=<secret>
PlaceContext__ManagedOAuth__Microsoft__Authority=organizations
PlaceContext__ManagedOAuth__Microsoft__RedirectUri=https://<workspace-origin>/api/managed-oauth/microsoft/callback
```

Grant delegated Microsoft Graph permissions `User.Read` and `Mail.Read`.
The application also requests `offline_access` for refresh tokens.

From **Settings → Connections**, select a project and **Connect Microsoft 365**.
The server issues a cryptographically random state and PKCE S256 challenge and
stores an encrypted verifier for ten minutes. The callback must arrive for the
same authenticated admin, tenant and state. The one-time state is consumed before
token exchange. Access and refresh tokens are encrypted in the project-specific
database row, refreshed with a row lock and never included in the response to the
browser.

The **Microsoft 365 mailbox sync** job starter explicitly opts in with:

```
PC_USE_MANAGED_MICROSOFT_GRAPH=true
```

The job runner resolves the project-scoped OAuth access token and injects
`PC_MICROSOFT_GRAPH_ACCESS_TOKEN` only into that runtime's transient environment.
No access token is stored in the job source, workflow definition or run snapshot.
The starter reads up to twenty recent messages' metadata from Microsoft Graph and
produces the normal JSON records output that a downstream data-quality gate can
consume.

**Security boundaries:** Anyone authorised to edit/execute arbitrary code jobs
within the connected project can access the delegated token if they enable this
marker. Limit job-editing rights appropriately. This initial integration does
not implement per-job connection grants, full provider token revocation, general
OAuth connector marketplace features, or a central cross-tenant callback broker.

**SaaS-scale callback limitation:** A single configured RedirectUri must match
the current workspace origin, and must be registered as a Web redirect URI
with Microsoft. Supporting unbounded automatically provisioned tenant subdomains
requires a central OAuth callback broker with securely bound browser sessions
before onboarding arbitrary SaaS tenants. Do not claim that arbitrary new
subdomains can connect automatically in this initial version.

## Run/plan edge cases requiring validation

- Two simultaneous starts against the last allowance: exactly one can reserve.
- Queued submissions do not bypass the reserve at execution start.
- Automatic job retries and workflow child jobs do not consume more runs.
- A replay uses one new execution key and consumes one allowance.
- A continuation of an existing paused workflow does not consume a new run.
- Duplicated and out-of-order subscription webhooks cannot grant extra runs.
- An upgrade in the same billing period preserves used runs.
- A new billing period starts the new paid allowance.
- A failed Microsoft token refresh does not expose credentials and requires reconnecting.

See the added unit tests for signature verification, PKCE S256, plan rules,
data-quality job configuration and workflow-run calls. These tests have **not
yet been executed in this environment** and real provider integration tests
remain required before production rollout.
