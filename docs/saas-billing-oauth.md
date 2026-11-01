# PlaceContext SaaS: unlimited workflow runs, managed throughput and OAuth

PlaceContext sells managed hosting, execution throughput, reliability and support
rather than a monthly number of workflow runs. Billing and usage live under
Settings → Billing; managed Microsoft authorisation lives under Settings → Connections.

## Plan catalogue

These are proposed PlaceContext USD prices, excluding taxes, not verified competitor
pricing. Actual prices must be configured in Stripe or agreed in signed contracts.

| Plan | Price | Workflow/job run starts | Admission throughput | Hosting |
| --- | ---: | --- | --- | --- |
| Developer Edition (`free`) | $0 | Unlimited | No application pacing | Self-hosted only, non-production; community support, no managed SLA |
| Basic Managed (`basic`) | $600/month | Unlimited | **60 starts/minute**, 1 per second | Shared managed; 99.9% SLA target by agreement |
| Standard Managed (`standard`) | $825/month | Unlimited | **600 starts/minute**, 10 per second | Shared managed; 99.9% SLA target by agreement |
| Enterprise (`enterprise`) | From $99,000/year | Unlimited | Per-tenant contractual rate | Dedicated or customer-hosted options; up to 99.99% SLA and TAM by contract |

Managed plans also enforce global concurrent top-level execution slots:
**Basic 2**, **Standard 8**, and **Enterprise contract-specific
`MaxConcurrentRuns`**. Workers wait for capacity; unlimited runs remain unlimited.
Kubernetes tenant namespaces also enforce tier-specific resource quotas:
Basic 8 pods/4 CPU/8 GiB, Standard 24 pods/16 CPU/32 GiB, Enterprise
80 pods/64 CPU/128 GiB as guarded defaults (commercial contracts can require
different infrastructure provisioning). Existing self-hosted defaults stay
unchanged. Kubernetes quotas reconcile when a tenant changes tier.

Leases heartbeat through PostgreSQL, expire after a worker crash, and
cancel work when ownership is lost. A paused human-input workflow releases its
slot; resumption reacquires without consuming another billed run.

These are **start admission rates**, not guarantees of execution completion time,
CPU speed, memory capacity, max concurrent jobs or Kubernetes isolation.
One top-level workflow or standalone job start is metered once; all child steps,
automatic retries and durable resumptions reuse the execution key. An intentional
manual replay is a new execution. A managed customer never reaches a *monthly*
volume limit; starts that exceed admission throughput wait.

### Idempotency after payload cleanup

New submission records retain a SHA-256 fingerprint of the initial payload
and sorted step overrides, even after encrypted run inputs are scrubbed from
completed receipts. This prevents conflicting idempotent retries without
retaining completed workflow inputs. Pre-existing records fall back to
comparing their still-retained encrypted inputs.

### Durable managed workflow submissions

On managed installations, the portal and authenticated management API now
**persist new workflow starts** in the existing encrypted PostgreSQL queue.
`POST /api/v1/workflows/{chainId}/trigger` returns HTTP 202 and a
tenant-scoped `GET /api/v1/workflow-submissions/{trackingId}` polling link;
self-hosted deployments retain synchronous responses. Workers restore
tenant and user context and keep the same run id on crash-before-start
retries. The durable handoff also preserves encrypted per-step input overrides.

Managed management API workflow replays also create durable queue receipts:
`POST /api/v1/workflows/{chainId}/replay` returns HTTP 202. The worker replays
from the requested failing step, preserving original run history and charging
only one new top-level workflow run. Clients can provide an
`Idempotency-Key` header to retry the same replay request without creating
another replay run. Individual replayed child jobs do not
consume additional billing or concurrency slots. A crash after a durable run
has already been established is reconciled without blindly repeating side
effects.

Distributed start pacing still occurs inside a queue worker, not the HTTP
request. Managed Core API standalone-job launches now also create their own
durable, encrypted and idempotent receipt, with HTTP 202 plus
`GET /api/core/v1/projects/{projectId}/jobs/{jobId}/submissions/{trackingId}`.
Workers preserve the run id across retries and avoid redispatch when the
job-run record already exists. The Core API submission receipt and original
run id are stable even when the caller retries an HTTP request with the same
`RunId`. Direct manual workflow replays and other
legacy direct-execution surfaces still need durable submission before launch.

### What the implementation actually enforces

- A PostgreSQL plan-row `FOR UPDATE` lock coordinates `NextDispatchAt` across replicas.
- Basic permits at most one new start per second; Standard ten per second; Enterprise is configured per tenant.
- Every accepted top-level execution gets an immutable ledger key in `billable_runs` and increments the 64-bit reporting counter `UsedRuns`. The ledger also stores the scheduled admission time, so cancelling and retrying the same execution cannot skip its original delay.
- The old `IncludedRuns` column remains for migration compatibility but is set to **0**, meaning unlimited. It is not used to reject starts.
- The admission delay happens in the execution handler after committing the reservation; no DB connection is held while waiting.
- Active subscription status is rechecked after the delay; cancelled/expired paid plans do not start more work.
- Queued RPC workflow submissions and scheduled jobs already use durable submission queues, but bursty direct UI/API calls may time out while waiting.

Fully durable admission backlog processing, fair tenant scheduling, real workload
concurrency caps, CPU/memory resource quotas and safeguards against costly
long-running jobs remain operational work. **Unlimited runs does not imply
unlimited infrastructure resources, free third-party APIs, or unlimited parallelism.**

### Self-hosted versus public hosted SaaS

Developer Edition is free and self-hosted, with no application-level pacing.
Do not market it as an open-source distribution until packaging and licensing
are verified. For a public managed deployment, configure both:

```sh
PlaceContext__SaaS__ManagedHosting=true
PlaceContext__SaaS__RequireExplicitTenantProvisioning=true
```

The first setting prevents a free/self-hosted Developer tenant from starting runs
on the paid SaaS service. The second prevents automatic workspace creation from
unknown subdomains. Both default to false for existing self-hosted installations.

## Stripe setup

Configure secrets and recurring price IDs server-side, never in the browser or
customer jobs. The following are placeholders, **not** actual Stripe IDs:

```sh
PlaceContext__Stripe__SecretKey=sk_live_...
PlaceContext__Stripe__WebhookSecret=whsec_...
PlaceContext__Stripe__BasicPriceId=price_basic_monthly
PlaceContext__Stripe__StandardPriceId=price_standard_monthly
PlaceContext__Stripe__EnterpriseSalesUrl=https://example.com/enterprise
```

Create monthly recurring Stripe prices for Basic ($600) and Standard ($825).
Checkout uses only configured server-side Price IDs; UI price amounts are
indicative and not fetched/verified against Stripe's product catalogue.
Stripe Billing Portal handles subscription management.

Enterprise is never sold through public Checkout. Following a signed agreement,
configure the tenant-specific Stripe price ID and positive start rate:

```sh
PlaceContext__Stripe__EnterpriseContracts__11111111-1111-1111-1111-111111111111__PriceId=price_enterprise_annual
PlaceContext__Stripe__EnterpriseContracts__11111111-1111-1111-1111-111111111111__MaxStartsPerMinute=1500
PlaceContext__Stripe__EnterpriseContracts__11111111-1111-1111-1111-111111111111__MaxConcurrentRuns=32
```

The GUID and Price IDs are placeholders. The enterprise subscription must carry
`metadata.tenant_id=<workspace-guid>`, match the configured PriceId, and use
quantity one. Only signed subscription events activate paid access. Legacy
Starter/Growth/Business price IDs are recognised for existing subscriptions but
cannot be purchased from the new plan screen.

Webhook endpoint: `https://<your-service>/api/billing/stripe/webhook`.
Listen for `checkout.session.completed`, `customer.subscription.created`,
`customer.subscription.updated`, and `customer.subscription.deleted`.
Only fresh HMAC-SHA256 signed events can update paid plans. Event IDs are
deduplicated and stale subscription events cannot roll back newer state.
Checkout success redirects do not grant paid execution.

## SLA and readiness limitations

The listed 99.9% / up-to-99.99% uptime SLA targets are **commercial goals**,
not achieved or guaranteed by this billing change. Dedicated hosting, customer
cloud AWS/Azure/GCP deployment options, compliance assurances, security controls
and dedicated technical account management must be delivered and contracted.

Before public launch, test Stripe Checkout/Portal, signed webhook ordering,
subscription cancellations during pacing, large backlogs, multi-tenant resource
fairness, self-hosted Developer deployment and quota-free reporting.
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

## Remaining test and launch work

- Verify unlimited ledger accounting and per-tenant pacing against PostgreSQL across replicas.
- Confirm direct UI/API backlog timeouts and migrate long waits to fully durable queued admission.
- Add per-tenant concurrency/pod/resource limits to complement start pacing.
- Complete live/test-mode Stripe checkout and signed-webhook integration tests.
- Complete Microsoft consent, callback and refresh tests with a configured provider.
- Finalize SLA remedies, billing currency, VAT/GST treatment and enterprise operations.
