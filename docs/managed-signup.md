# Managed PlaceContext sign-in and workspace signup

## User experience

- **Managed apex** (\`https://placecontext.io/\`): redirects to **Find workspace** on
  \`/login\`. Enter an existing workspace slug to continue to \`https://slug.placecontext.io/login\`.
  No arbitrary user-entered URL is used for the redirect.
- **New managed workspace**: **Create workspace** on \`/signup\` collects its name,
  unique address, owner work email, 12+-character password and a Basic or Standard
  plan preference. Plan choice is not an entitlement or a payment authorization.
- **Verification**: a unique single-use, 30-minute email link first opens a
  confirmation screen. Only the CSRF-protected **Confirm** POST provisions the
  tenant, hashed owner password and default project together in one PostgreSQL
  transaction, including the standard tenant access roles. A second, short-lived,
one-use code signs in on the **tenant's own
  host**. No wildcard-domain auth cookies are required.
- **Cloud setup**: verification also queues a durable provisioning record in the same transaction.
  Owners land at `/cloud`, where setup progresses through Setting up, Ready or Setup failed.
  The worker prepares the tenant's restricted Kubernetes namespace and plan resource quota
  without launching workloads. Failures retry automatically up to five attempts with backoff;
  workspace administrators can retry after that. Expired worker leases are recovered.
- **Activation and usage**: the Cloud page provides Stripe checkout, subscription management,
  and run/AI request usage. Only a signed subscription webhook enables execution;
  signup, provisioning success and checkout redirects do not grant entitlements.
- **Existing tenant**: email/password sign-in continues to support the existing
  provider-mandated 2FA flow, invited members and configured company SSO. Invalid
  credentials remain generic.

## Required hosted configuration

\`\`\`text
PlaceContext__SaaS__ManagedHosting=true
PlaceContext__SaaS__RequireExplicitTenantProvisioning=true
PlaceContext__SaaS__Signup__Enabled=true
PlaceContext__PublicBaseUrl=https://placecontext.io
PlaceContext__SaaS__Signup__PostmarkServerToken=<from-secret-store>
PlaceContext__SaaS__Signup__FromEmail=hello@placecontext.io
PlaceContext__SaaS__Signup__FromName=PlaceContext
PlaceContext__Stripe__SecretKey=<from-secret-store>
PlaceContext__Stripe__WebhookSecret=<from-secret-store>
PlaceContext__Stripe__BasicPriceId=<configured-price-id>
PlaceContext__Stripe__StandardPriceId=<configured-price-id>
\`\`\`

The shared apex allows only its login/signup forms, health checks and the
public Blazor hub (which still enforces the page authorization policies).
Requests to workspace data APIs, billing and the signup ticket-completion
endpoint must resolve a real tenant subdomain.

The sender uses a **platform-owned** Postmark transaction stream; it does not
depend on a tenant's yet-to-be-created communication preferences. Its credentials
must not be committed or put into sample env files. Configure verified sender
domain/SPF/DKIM and actual delivery before public launch.

Self-service signup is **off by default**, and requires managed hosting plus
explicit tenant provisioning. The historical \`/auth/portal\` machine-operator
login and the \`/auth/setup\` / \`/auth/setup/cli\` first-admin routes are disabled
on managed installations. Existing self-hosted installs retain their original
\`/setup\` flow and machine-operator provisioner.

In an isolated Development test host only, the additional flag
\`PlaceContext__SaaS__Signup__AllowUnverifiedDevelopmentSignup=true\` allows
an automated UI run to bypass external email delivery. This flag has **no effect
outside Development**. It must not be set in managed public production.

## Security and rollout

Canonical public origins are limited to \`placecontext.io\` and the dedicated
\`localhost\` loopback development domain currently supported by the
tenant-resolution middleware. Signup redirects must never be constructed from
incoming Host, X-Forwarded-Host or form values. Use trusted ingress routing for
\`*.placecontext.io\` and TLS. The signup verification/tenant-login codes are
generated from 256-bit CSPRNG values and stored by SHA-256 digest only.
CSRF tokens protect state changes; public routes have IP-partitioned rate limits.
A failed verification never grants Stripe entitlements.

Before allowing public traffic, validate Postmark delivery, delivery/retry
behavior, signup abuse controls, real Stripe Checkout/webhook and production
TLS, DNS, monitoring and cleanup/retention of expired pending records.
Public registration is a separate **launch toggle**; the code merge does not
automatically enable it.

## Cloud limits and provisioning

The Cloud offering uses a shared platform and database with tenant-scoped data and
separate Kubernetes execution namespaces. It does not create a dedicated application
or database server for each customer. Cluster credentials, ingress/TLS and the shared
platform are installed by the operator before public registration is enabled.

The following operator settings apply only when `ManagedHosting=true`:

```text
PlaceContext__SaaS__BasicRunsPerPeriod=10000
PlaceContext__SaaS__StandardRunsPerPeriod=100000
PlaceContext__SaaS__BasicAiRequestsPerPeriod=5000
PlaceContext__SaaS__StandardAiRequestsPerPeriod=50000
```

All limits must be positive. They are per tenant, per subscription billing period;
configure monthly Stripe prices for monthly Basic and Standard allowances. Enterprise
contracts use `RunsPerPeriod` and `AiRequestsPerPeriod` in the existing tenant-bound
Stripe contract configuration (defaults 1,000,000 and 500,000 per contract period).
Annual subscription periods therefore have annual allowances, not monthly resets.
Self-hosted execution stays unlimited.

Run reservations are atomic across replicas and share an idempotency ledger. Existing
reservations remain valid when the last slot is used; new executions stop at the cap.
Upgrades preserve usage within the period, and verified renewals reset the counters.
AI HTTP attempts through the platform's model, browser-agent and TypeSafe clients are reserved before
sending. This covers chat, agents, reviews and browser actions; failed attempts count.
Model discovery GET requests do not count. Hosted tenants cannot use host CLI accounts.
Cloud browser/question agents must use `PlaceContext:Qemu:Agent:Provider=chat-completions`;
managed startup rejects host CLI providers. AI requests are not a dollar/token spending cap and do not meter arbitrary external
API calls made by customer job code. Provider-side budgets remain necessary for those.

Migration `20261117000000_CloudWorkspaces` adds the provisioning queue and AI counter.
Provisioning retries reconcile existing resources without deleting customer data.
The worker logs technical failure details; the portal only shows a generic setup failure.
Provisioning does not activate a subscription or bypass runtime entitlement checks.

Kubernetes namespace quotas apply to Kubernetes jobs. Host-local Docker build/run
and browser VM execution retain their existing individual limits; they do not
become Kubernetes-managed through this change. Provision suitable execution hosts
and audit those runners before enabling them for public tenants.
