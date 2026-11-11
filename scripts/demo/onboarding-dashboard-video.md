# PlaceContext onboarding video — current dashboard, real agents

**Replace the earlier-build footage** in `placecontext-onboarding.mp4`. The scene around **00:32** shows a dashboard overview that no longer corresponds to the current `Dashboard.razor`/`BuildChat.razor` implementation. The earlier walkthrough does not demonstrate a live automatic multi-agent run. Do not use an illustrative agent response or a completed historical run to imply live automation.

## Target edit: 60–90 seconds

| Time (approx.) | Record from the running app | Message |
| --- | --- | --- |
| 0–6 s | Corgi branding; cut into the current dark-mode project dashboard | **Put agents to work. Keep people in control.** |
| 6–16 s | Chat-first dashboard, agent selector, **Agent → Sub-agents** listing | **Give specialists their own responsibilities.** |
| 16–26 s | Dashboard **Run a workflow** picker, select a real multi-agent workflow | **One workflow. Multiple agents. Shared context.** |
| 26–42 s | Live workspace/run preview; query result, **Researcher** task and recorded model activity | **Start with data. Let the agents work.** |
| 42–54 s | **Reviewer** agent activity, tool result/JSON and outcome | **Inspect what each agent did.** |
| 54–66 s | Human approval checkpoint, operator chooses Yes | **A person decides what happens next.** |
| 66–80 s | **Handoff writer** executes, run Succeeded; output preview | **From evidence to a reviewed result.** |
| 80–85 s | Closing card with product name and docs | **PlaceContext. Agentic workflows and data.** |

Use the [three-agent example](../../docs/multi-agent-workflows.md): `Query data → Researcher → Reviewer → Human in the loop → Handoff writer`. The source examples use three *fictional* customer accounts. Query data may appear at **any stage**; the first query here is only an example. The automatic agents must each use a configured model connection. A live run must show recorded agent/tool activity; do not substitute an external-agent handoff.

## Capture rules

1. Use a running, current PlaceContext application, not the earlier dashboard or an HTML mock. Set dark mode in the app and verify **Dashboard** renders the `.build-chat` workspace.
2. Select an existing configured sample project, never production customer data. Verify credentials and provider readiness before starting; model usage may incur provider charges.
3. Record at 1920×1080. Give the Monaco editor time to mount and highlight if it is shown. Avoid jump cuts that hide model activity or pretend one agent's output belongs to another.
4. Capture actual model/tool updates and inspectable JSON; include the human checkpoint and both the chosen and skipped decision paths. Show `Succeeded` only after the engine reports it.
5. Preserve provenance: source revision, app version, actual run status and whether automatic models or external agents were used. Do not invent execution status, run durations, or data.
6. Use `scripts/demo/capture_live_dashboard.mjs` for the browser footage. It validates the current chat-first dashboard and refuses to publish if configured sub-agents, persisted agent activity or Succeeded status are not seen. An operator drives the real workflow and approves the sample checkpoint.

## Local capture

```bash
npm install --no-save playwright
npx playwright install chromium
PC_PROJECT_ID=<project-guid> \
PC_URL=http://localhost:7700 \
PC_SOURCE_REVISION=$(git rev-parse --short HEAD) \
node scripts/demo/capture_live_dashboard.mjs
```

For an already authenticated browser session, pass `PC_STORAGE_STATE=/path/to/storage-state.json`; keep that file private. The recorder generates screenshots, an authenticated browser video, a compressed MP4 if `ffmpeg` is available, and `capture-evidence.json` under `artifacts/onboarding-dashboard/`. Review for secrets and personal data before publishing. The new captured segment should **replace**, not merely caption, the retired-dashboard segment in the old MP4.