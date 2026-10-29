# Implementation checkpoint — 26 October 2026

## Working tree

Yesterday's implementation remains in the working tree, based on commit `7e51fe5f`
(`Model interactive and QEMU workflow actions`). Many files are modified or untracked;
preserve them when resuming. No commit or deployment was made during this continuation.

## Implemented

- Workflow editor steps for human questions, including Yes/No responses, and external-agent
  hand-offs. Agent steps pause until a response arrives.
- QEMU steps using a prepared Linux desktop image, non-root `workflow` execution, disposable
  disks, bounded output, timeout and cancellation cleanup. Instructions now mean natural-language
  prompts handled by a host-side agent loop; desktop streaming is not implemented.
- Workflow portal URLs under `/project/{projectId}/workflows`, Observability links using
  `tab=workflows&workflowRun=...`, and workflow API aliases. Legacy chain URLs remain supported;
  MCP tool and payload identifiers retain their existing names.
- Durable workflow queues with delayed delivery, retries, expiry, and an SQS-style interface.
  Message inspection opens the workflow run and job logs within the queue page.
- Job logs, log field mapping, and query pipelines including Azure-style `contains` operators.
- Side panes across the app, Jobs pane lifecycle fixes, and wider Observability panes.
- Networking for own Kubernetes workers: VPCs with private IPv4 CIDRs, security groups with
  ingress/egress TCP/UDP rules, and per-job assignments. The Queues-style UI has VPCs,
  Security groups, and Job assignments tabs, search, VPC filtering, and separate side-pane editors.
  Security groups use selectable table rows with inbound/outbound rule tables below. VPC names
  and CIDRs are immutable; confirmed deletion requires no assignments and a current version.
- Kube-OVN VPC/subnet provisioning and per-run Kubernetes NetworkPolicies; unsupported
  workers reject assignments. Configuration is tenant/project scoped, uses optimistic concurrency,
  and is included in backup/restore and run snapshots.
- QEMU profile discovery in the workflow editor and a Debian desktop image builder under
  `deploy/qemu`. The standard kernel is required because genericcloud omits virtio graphics.
- Embedded help now describes Workflows, human/agent/QEMU steps, queues, and worker networking.

## Network flow continuation

- Network flow tab: a scrollable canvas for inbound CIDRs, the selected VPC/security group,
  assigned jobs, outbound CIDRs, and default-deny paths. Select a rule to prepare a test.
- Rule checks load the saved policy and match direction, TCP/UDP, IPv4 CIDR, and destination
  port. Matching rules (or the default-deny path) are highlighted. Results show target and version.
- Live outbound TCP probes execute trusted Python code through the existing worker runner,
  using the selected VPC/group and normal worker placement. One connection, no payload, an
  eight-second connection timeout, a two-minute overall deadline, and cancellation on navigation.
- Results distinguish connectivity observations from saved-rule predictions. Worker startup
  failures are not reported as firewall blocks. Unexpected successful connections are flagged.
- Kubernetes temporary resource cleanup now also covers cancellation/failure during ConfigMap
  and NetworkPolicy creation before the Job is created.
- Local probe-script checks verified connection, refusal, and no application payload. Browser
  checks exercised inbound/outbound matching, selection, invalid targets, and unsupported-worker
  feedback. Live Kube-OVN connectivity remains unverified: this local host runs Docker workloads.
- Final focused checks: 24 flow-service tests, 31 Kubernetes-runner tests, and 6 Host tests passed.
  `git diff --check` passed.
- Tests support unicast IPv4 addresses. Live tests are outbound TCP only; no DNS, TLS/HTTP,
  UDP response probing, inbound listeners, or packet capture. No dependencies added.

## QEMU prompt-agent continuation

- QEMU instructions are now prompts, never executed verbatim as shell text. `QemuPromptAgent`
  runs a bounded observe/tool/result loop through an operator-configured chat-completions endpoint.
- The model receives task/context, the current screenshot, and bounded tool output. It can call
  guest_shell, desktop_screenshot, and finish. No host-shell or host-file tools are exposed.
  Provider credentials remain in host configuration and are not passed into the guest.
- Guest commands keep the existing non-root/capability restrictions and have a 30-second limit.
  The VM lifetime deadline covers boot, model calls, and tools. User cancellation propagates to both.
- Final summary, failure reason, model turn count, and tool activity reach the workflow output.
  A prose-only model response cannot complete a workflow. Missing model configuration fails before
  booting a VM and is reported in the editor.
- Added scrot and xdotool to both the builder and the local desktop image. Existing serialized
  `command` fields remain compatible structurally but now mean prompts; review old script steps.
- HTTP models can use a vision/tool-capable chat-completions endpoint without an SDK. The local
  app now uses the Codex provider described below; no separate API key is configured.
- Final checks passed: 11 agent/runner tests, 11 workflow/backup tests, and 6 editor/wiki tests.
  Two opt-in real-KVM tests also passed, covering the production agent loop with scripted model
  replies (Chromium interaction, screenshot, completion), non-root isolation, fresh disks, timeout,
  and cancellation. These checks used scripted model replies; the real Codex check is recorded below.
- Browser check passed for the task prompt editor, configured image selection, missing-model
  notice, and adding a draft step without saving a workflow. No browser exceptions were reported.

## Local Codex continuation

- Added `PlaceContext:Qemu:Agent:Provider=codex`. The existing bounded agent loop communicates
  with `codex app-server` over private stdio, using the host account's Codex login. No copied
  credential, HTTP bridge, new dependency, or listening API port is required.
- Codex CLI 0.153.1 and the configured `gpt-6-astra` model passed a real tool-call smoke test.
  Host shell/browser/computer tools, plugins, hooks, configured MCP servers, and host skill
  discovery are disabled. Threads are ephemeral with an empty working directory and read-only
  sandbox; unknown host-operation requests fail closed. Cancellation stops the child processes.
- 16 focused protocol/agent/runner tests passed. The opt-in real Codex + QEMU test passed in
  41 seconds: created a unique offline HTML page, opened Chromium, inspected a screenshot,
  and returned the exact title. A separate non-root guest check verified the browser window.
  The test also verified that disposable VM directories were removed.
- Six Host editor/wiki tests passed. The app was restarted with Codex enabled; `/healthz`
  returned 200 and a browser check confirmed `Agent model: gpt-6-astra` with no setup warning
  or browser errors. Startup configuration is shown below.
- Actual inference uses the signed-in account remotely and consumes its usage allowance.
  The experimental app-server dynamic-tool API is version-sensitive; rerun the documented
  integration test after updating Codex. Remote worker Codex logins were not configured/tested.

## Local startup

The existing `devcontext-validation-postgres` Docker container retains yesterday's database
on a Docker volume and exposes PostgreSQL on `127.0.0.1:5433`.

```bash
docker start devcontext-validation-postgres
dotnet run --project src/PlaceContext.Host --no-launch-profile -- --environment Development
```

The portal is at `http://localhost:7700`; `/healthz` returned HTTP 200 on 26 October.
The host defaults to binding all interfaces; set `ASPNETCORE_URLS=http://127.0.0.1:7700`
if loopback-only access is desired. The browser was opened to the portal.

QEMU remains disabled by default in configuration. This local app was launched with:

```bash
env PlaceContext__Qemu__Enabled=true PlaceContext__Qemu__UseKvm=true \
  PlaceContext__Qemu__Images__browser-linux=/home/brad/code/devcontext/dist/qemu/browser-linux.qcow2 \
  PlaceContext__Qemu__Agent__Provider=codex PlaceContext__Qemu__Agent__Model=gpt-6-astra \
  dotnet run --project src/PlaceContext.Host --no-build --no-launch-profile -- --environment Development
```

`dist/qemu` is ignored by Git. It holds the verified Debian input, SHA512 checksums, build log,
and flattened desktop image. The image was built, then updated with Debian's standard kernel
and GRUB selection after the desktop test exposed the cloud kernel's missing graphics driver.
The same fix is in the builder template. A full clean image rebuild after that fix was not repeated.
See `deploy/qemu/README.md` for reproduction and the opt-in real desktop test.

## Validation

- 14 focused Application tests passed, including network assignment through map/reduce and
  snapshot capture, backup roundtrip, and failed network restore keeping imported jobs deny-egress.
- Domain CIDR/rule validation passed, including rejection of host bits in a network CIDR.
- 5 worker networking tests passed: real PostgreSQL persistence, workspace isolation, immutable
  CIDRs/group IDs, stale updates, permission checks, unsupported runners, NetworkPolicy rules,
  and simulated Kubernetes API provisioning/ownership checks.
- Real KVM desktop test passed with Chromium's sandbox enabled: non-root credentials, cleared
  capabilities, no-new-privileges, JSON input, GUI page, fresh disks, timeout, cancellation, cleanup.
- Browser check passed: VPC creation/invalid CIDR, separate group creation and rule editing,
  tab/reload persistence, assignment-pane opening, selectable group rows and inbound/outbound
  rule tables, read-only VPC fields, cancel/delete confirmation, desktop/mobile layout, no browser
  exceptions. The temporary browser-test VPC was deleted through the UI.
- Host build passed. `git diff --check` and image-builder shell syntax passed.
- 7 focused Host tests passed, including group editor isolation/cancellation, workflows, and wiki.

Test results are under `/tmp/devcontext-test-results`. Existing test-project warnings include
an EF Relational version conflict and the dependency audit warning for
`System.Security.Cryptography.Xml` 10.0.11. Earlier full-suite runs recorded 12 baseline failures;
the full suite was not repeated. The focused architecture checks still report the pre-existing
ProjectData service/JS injection and TraceWaterfall missing ViewModel boundaries.

## Remaining operational limits

- No worker cluster CNI was installed or replaced. Set up Kube-OVN with NetworkPolicy enforcement,
  apply `deploy/release/k3s/worker-networking-rbac.yaml`, and enable
  `PlaceContext:WorkloadRunner:KubeOvnEnabled`. Live multi-node traffic enforcement is unverified.
- Custom VPCs start isolated. Gateways, DNS, peering, and ICMP rules are not managed.
  Deleting a VPC removes its saved definition and groups; provisioned cluster resources are
  retained for in-flight runs and require operator cleanup when unused.
  One subnet per VPC and one security group per job are supported. Do not apply broad allow
  policies to the workload namespace; Kubernetes policies combine their allowed traffic.
- QEMU steps run on the workflow host with their own network toggle. Job VPC assignments do not
  cover QEMU. Prompt execution requires a configured vision/tool model or Codex login. Desktop streaming is
  not implemented.

Useful entry points: `NetworkingViewModel`, `DbWorkerNetworkService`, `KubeOvnNetwork`,
`QemuEnvironmentRunner`, `JobChainsViewModel.Qemu`, `QueuesViewModel`, and `DbChainQueueService`.
