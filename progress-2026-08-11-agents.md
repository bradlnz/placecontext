# Progress — Agents command canvas and collaborative orchestration

Session timestamp: `2026-08-11T08:38:14+10:00`

## Status: implementation complete; independent review and endpoint-security audit in progress

### Delivered
- Added a project-scoped **Agents** workspace at `/project/{projectId}/agents`, protected by `agents.manage`.
- Added a visual orchestration canvas with one central Command Agent and configurable worker agents beneath it.
- Added reusable Research, Job Operator, Analyst, and MCP Specialist templates.
- Added explicit capabilities and per-agent Job allowlists; the data graph is mandatory for every agent.
- Added persistent `agent_definitions` storage and EF migration `20260810222742_AddAgentDefinitions`.
- Added collaborative Command Agent orchestration: it can select up to four least-privileged workers, run their graph-grounded contributions concurrently, and synthesize them into one response.
- Restricted tool execution to the active collaborators' combined capabilities and Job allowlists.
- Hardened Chat and launchpad Job/chain execution against cross-project IDs.
- Routed project chat through `LLM_API_TOKEN` from the encrypted project Vault when present; otherwise it uses the local agent cluster. Plaintext tokens are never returned or persisted outside request construction.
- Routed Agent Chat and unattended agent sessions through the Command Agent.
- Added Agents navigation, settings labels, route helpers, responsive styling, and Wiki documentation.

### Verification so far
- Host production-source build: successful, 0 errors.
- Agent domain tests: 3 passed.
- Focused agent application tests: 24 passed.
- Application suite baseline: 462 passed, 4 existing Job-chain payload expectation failures unrelated to this feature.
- Infrastructure test project has existing unrelated compile blockers in unfinished `OpenSearchDataGatewayTests.ExportIndexAsync` coverage; the new Vault gateway test remains present for the repaired suite.
- `git diff --check`: clean.
- Added no hardcoded secrets or plaintext credentials.

### Remaining
1. Resolve all findings from independent backend/UI/test reviewers.
2. Audit every HTTP, OAuth, MCP, and public-ingestion endpoint for authentication, permission policy, tenant/resource isolation, CSRF/signature/token validation, and rate limiting; add regression tests and lock down findings.
3. Run final focused/broad verification, commit verified pieces separately, deploy with `deploy.sh`, and validate production artifacts and behavior.
