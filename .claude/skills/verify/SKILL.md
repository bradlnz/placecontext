---
name: verify
description: How to build, launch, and drive PlaceContext (portal, MCP, TUI) locally to verify changes end-to-end.
---

# Verifying PlaceContext changes

## Launch the Host locally

Port 7700 is usually held by the k3d dev cluster's load balancer — use another port:

```bash
dotnet build src/PlaceContext.Host --nologo -v q
ASPNETCORE_URLS="http://localhost:7710" ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-build --no-launch-profile --project src/PlaceContext.Host   # background it
```

Development mode auto-signs the browser into the portal (no password). Wait for
`curl -sL http://localhost:7710/` → 200. The local instance uses the `placecontext`
Postgres dev DB (NEVER drop it) and the DockerWorkloadRunner (no cluster).

## Create a project (needed before any jobs UI exists)

Projects only register via MCP. `/mcp` needs an OAuth bearer (authorization_code + PKCE only).
Working helper scripts from a past session: `oauth.sh` + `mcp.sh` — the dance is:
dev cookie via `curl -c jar -L /`, POST `/connect/register` (token_endpoint_auth_method none),
GET `/connect/authorize` with PKCE S256 + the cookie (302 back with code), POST `/connect/token`.
Then MCP Streamable HTTP: initialize → grab `mcp-session-id` response header → notifications/initialized
→ tools/call. `create_project` takes `{name, path}` (the param is `path`, not `rootPath`).

## Drive the portal (Blazor Server — needs a real browser)

`npm i playwright` in the scratchpad + `npx playwright install chromium-headless-shell`.
Deep links to `/project/{id}/jobs` can bounce to Overview on a fresh session — navigate by
clicking: home → project name → `.dctab` (Jobs / Chains / …). Slide-out panels are `.dcslide`,
cards `.dccard`, tab buttons `.dctab`, footer `.dcslide-foot`.

To verify binary artifact integrity: create an inline-code python job whose source writes a
binary PDF to /out and prints `{"sha256": ...}` to stdout, run it, open the run detail, decode
the `⬇ report.pdf` data-URI href, and compare hashes. `python:3.12-slim` must be pulled first.

## Drive the TUI

`cd deploy/tui && go build ./...` then run `./pctl-tui` inside an isolated tmux
(`tmux -L name new-session -d -s tui -x 160 -y 42 './pctl-tui'`). It talks to the k3d
cluster (`k3d-placecontext` kubeconfig context) and reads runs straight from the in-cluster
DB via `kubectl exec deploy/placecontext-db -- psql`, so cluster runs are browsable without
redeploying the host image. Dashboard: ~25 Downs reaches the JOB rows; ⏎ opens runs; ⏎ opens
a run's detail; `v` opens the JSON tree.

## Gotchas

- MinIO/object store is disabled in plain local runs — "Post-job outputs" (RunArtifactLink)
  cards and `/runs/{id}/artifacts/{id}` streaming can only be observed in-cluster.
- The k8s runner path (pod-log framing) can't be exercised locally; its parser has unit tests
  (`KubernetesWorkloadRunnerTests`).
- Docker runner treats `/out/result.json` as the primary artifact; the k8s runner uses stdout.
  Same job code can show "(no artifact)" locally but an artifact in-cluster.
