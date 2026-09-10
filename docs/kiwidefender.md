# KiwiDefender .NET integration

The Host references the actual `KiwiDefender` 0.1.0-preview.1 NuGet package
from `vendor/nuget`; normal restore and Docker builds include it. No external
KiwiDefender service is required to evaluate a local policy.

Set `KiwiDefender__PolicyFile` to a trusted local policy file to activate the
integration. With no path configured, existing application behavior is retained.
Invalid/unreadable policies fail startup; an expired active policy returns 503
for protected requests. Policies load at startup; restart the Host after replacing
the file. Mount it read-only in containers, outside the public web directory.

The middleware runs after ASP.NET authorization and user resolution. It checks
authorized controller actions and the `/mcp` HTTP transport before dispatch.
It does not intercept Blazor circuit operations, background jobs or individual
MCP tools; existing tool and application permissions continue to enforce access.
Public/authentication endpoints retain their existing behavior.

Policy application: `placecontext`; operation: `http.request`; action: exact
HTTP method; resource: `ControllerName.ActionName` (without `Controller` suffix),
or `mcp` for the MCP transport. The only SDK scope supplied is `authenticated`,
established by ASP.NET, never a request header or unverified token. Identity
uses the verified subject and tenant claim, with the resolved tenant as the target.
Workspace API keys without a tenant claim use their resolved request tenant.
Validated credentials without an expiry claim/ticket get a 60-second request-local
freshness bound; every new request must still authenticate normally.

To create a seven-day observation policy in a private directory:

```sh
umask 077
python3 - <<'PY' > kiwidefender-policy.json
import json, time
now = int(time.time())
print(json.dumps({
    "version": 1, "application": "placecontext", "generation": 1,
    "not_before": now - 1, "expires_at": now + 7 * 86400,
    "mode": "observe", "blocked": [], "rules": [],
    "rate": {"limit": 120, "window_seconds": 60, "max_subjects": 10000}
}, indent=2))
PY
export KiwiDefender__PolicyFile="$PWD/kiwidefender-policy.json"
```

This starter records `would_block` for each authorized request because it has no
allow rules. Add explicit rules using the resource/action names in Host logs,
then change mode to `enforce` when ready. For example, this rule admits the
workspace search endpoint after its existing DataRead authorization succeeds:

```json
{"id":"search-read","effect":"allow","operation":"http.request",
 "action":"GET","resource":"Search.Search","scope":"authenticated"}
```

Enforce mode denies unlisted operations (403), rejects invalid identity (401),
and applies the configured subject/tenant quota to allowed operations (429 with
Retry-After). Observation mode admits policy denials while logging `would_block`;
invalid identity and unavailable/expired policy still fail closed. Rates and
policy generation tracking are process-local, not shared across replicas/restarts.

Structured Host logs contain resource, method, outcome, reason, generation and
rule ID. They omit tokens, request bodies and arguments. SDK decisions are not
automatically exported into the KiwiDefender portal by this integration.

Verify: `dotnet test tests/PlaceContext.Host.Tests --filter FullyQualifiedName~KiwiDefenderMiddlewareTests`.

Validation on 2026-09-10: Host build and all 14 KiwiDefender integration cases
passed; upstream SDK conformance checks passed (17 decisions, 33 policy JSON
cases, quota concurrency, guards and bounded events). The full Host suite
reported 280 passing / 12 failing tests. Failures are existing source-based UI
contracts for Chat, ProjectData, Jobs, Observability, settings and shared
components, all untouched by this integration. The test build also reports an
existing EF Core Relational 10.0.4/10.0.11 version conflict warning.

The integration is optional and disabled by default, including the placecontext.lan
rollout. Deploying the package does not activate policy checks; activation requires
KiwiDefender__PolicyFile as described above.
