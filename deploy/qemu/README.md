# QEMU desktop images

The workflow runner accepts prepared Debian-compatible x86-64 qcow2 desktop images and a
natural-language task prompt. The default browser path uses frames from the live VM display, without reading HTML or enumerating DOM controls.
The configured vision model proposes a coordinate action; Jev judges it against the image before execution.
Our Rust guest agent controls the installed Chromium through a private DevTools pipe and uses
the existing guest-agent channel for host-side model calls. No Python, Node.js, npm, Playwright,
or external browser-agent runtime is required. Commands run as `workflow`,
with capabilities and supplementary groups dropped and `no_new_privs` set. Every run uses a
disposable disk; no model-generated command is run on the application host.

## Build a desktop image

Install QEMU, `qemu-img`, and `cloud-localds`. Download the Debian 13 amd64 genericcloud qcow2
image and its `SHA512SUMS` from the [Debian cloud image directory](https://cloud.debian.org/images/cloud/trixie/latest/).
Verify the image against that checksum before using it. Keep the downloaded image and
checksum together so the input can be reproduced; the `latest` directory changes over time.

```bash
bash deploy/qemu/build-desktop-image.sh debian-13-genericcloud-amd64.qcow2 browser-linux.qcow2
```

The builder installs X11, Openbox, Chromium, xdotool, and the QEMU guest agent. It selects Debian's standard
kernel because the cloud kernel omits the virtio graphics driver. It creates a password-locked
`workflow` user without sudo access and enables desktop autologin. Provisioning uses outbound
internet access to Debian's package repositories. It uses KVM when available, otherwise software
emulation, and stops after 30 minutes. Progress is written to `browser-linux.qcow2.build.log`.
The output image is flattened and never overwrites an existing image.

The base cloud image is trusted input. Keep both it and the final image writable only by the
operator, and run the application itself as a non-root account. Do not put credentials into the
image. The build image has no SSH login credentials and disables root/password SSH login.

## Jev browser tasks (default)

Set these on each workflow host:

```text
PlaceContext__Qemu__Enabled=true
PlaceContext__Qemu__Images__browser-linux=/absolute/path/browser-linux.qcow2
PlaceContext__Qemu__UseKvm=true
PlaceContext__Qemu__Executor=jev
TYPESAFE_API_KEY=your-key
PlaceContext__Qemu__Agent__MaxSteps=20
```

`Executor` defaults to `jev`. The model and key reuse `PlaceContext:TypeSafe`; the default model
is `jev-1.14.0`. Existing Debian images built by this guide already provide Chromium;
no image rebuild is needed. The statically linked `pc-jev` executable is bundled with the host
application and copied into each disposable guest in bounded chunks. Chromium keeps its sandbox
enabled. Rust communicates with Chromium over inherited pipes; no HTTP server or debugging
port is opened. Automation reads the same private VNC framebuffer as the live preview; it no longer
calls browser screenshot APIs or runs screenshot commands in the guest. A bounded five-frame-per-second
feed keeps one desktop framebuffer in memory. Each model decision waits for a frame requested after
that decision begins, then encodes it as a 1280×800 PNG for the image-based model API. Frames include
browser chrome and native dialogs. Rust maps coordinates back to the real desktop dimensions and
uses xdotool for mouse, keyboard and scroll input. CDP only manages browser navigation, tabs and
profile lifecycle. The task loop, input validation and host bridge remain Rust.

Linux source builds now require Rust/Cargo (tested with 1.98) and a C linker with static libc.
The Infrastructure project builds the x86-64 Linux guest executable incrementally and copies it
alongside the .NET output, including published output. The Dockerfile builds it in a separate
Rust stage for both application host architectures; runtime images do not contain Rust tooling.
The only direct Rust dependency is `serde_json`, with transitive versions committed in `Cargo.lock`.

Include the starting HTTP(S) URL in the task prompt. For example:

> Start at https://your-order-desk.example. Order two Team kits for buyer@example.test.
> Wait for the order to become ready, verify the total and shipping, then refresh and check
> the saved order. Report the order ID, final status, total, and shipping.

Enable the VM's network toggle for sites outside the guest. Model calls use the host connection
and still work when guest networking is disabled. Neither TypeSafe nor vision-model credentials
are copied into the VM. Live desktop frames, task instructions, recent actions and workflow context are sent to the configured vision model
and TypeSafe. The same decision frames are saved in the replay store with pointer targets and click markers.

Configure `PlaceContext:Qemu:Agent` with a vision model: either `Provider=codex` with a signed-in
local CLI and `Model`, or `Provider=chat-completions` with an image-capable `Endpoint`, `Model`, and
optional `ApiKey`. The model proposes one action and transcribes visible evidence; Jev independently
judges the actual live desktop frame. Keys stay on the host. Do not put credentials in prompts or context.

Jev image requests use `TypeSafe-Danger-Zone-Images: true`. TypeSafe currently labels image input
experimental; see the [image API](https://docs.typesafe.ai/images). Reversible browser actions run only when Jev selects the approval choice; its confidence is recorded. Completion requires a proposed finish and Jev goal probability above
0.85. These thresholds are gates, not proof of correctness. Invalid responses and exhausted budgets
fail the step. Grocery offers include independent image judgments for price, total pack size,
product match, selected postcode and availability, plus the evidence frame and image SHA-256.

The runner supports coordinate clicks, replacing field text, Enter, scrolling at a specified point,
back navigation and waiting. Pointer movement is interpolated before input. Replay displays the
recorded targets and click markers over decision frames, with a larger full-width view and full-screen
mode. Replay remains a sequence of recorded actions. For manual login, password entry and
other desktop interaction, use the live human-control console described below. Automated file
uploads, new-window workflows and arbitrary desktop commands remain outside the browser agent.

Realistic Order Desk task inputs and expected results are in
[`examples/order-desk/tasks.json`](../../examples/order-desk/tasks.json). Replace the URL placeholder
with the deployed service URL before copying a task into a workflow. The guest cannot use a host's
`localhost` URL; supply an address reachable from the VM.

For a live research workflow, see the [Australian grocery comparison demo](../../examples/grocery-compare/README.md).
It takes a product name, defaults to postcode 4101, inspects Coles and Woolworths in QEMU,
uses Jev to assess captured price evidence, and produces a report comparing verified offers.

Run the deterministic tests without a real model:

```bash
cargo test --locked --manifest-path src/PlaceContext.QemuAgent/Cargo.toml --target-dir dist/qemu-agent-build
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~JevQemuTaskAgentTests
PLACECONTEXT_TEST_QEMU_IMAGE=/absolute/path/browser-linux.qcow2 \
PLACECONTEXT_TEST_QEMU_KVM=true \
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~JevQemuIntegrationTests
```

The VM test uses real Chromium,
a local order form served by a test-only Python fixture, scripted image-model/TypeSafe responses, and replay capture. It checks browser
execution and isolation, not the quality of a live Jev model or the full platform queue path.

## Legacy desktop agent configuration

Select `PlaceContext__Qemu__Executor=model` to retain the general guest-shell agent, which also observes the live desktop feed.
This is also required for existing general desktop tasks that do not include a starting URL.

Set these on each host that executes workflows:

```text
PlaceContext__Qemu__Enabled=true
PlaceContext__Qemu__Images__browser-linux=/absolute/path/browser-linux.qcow2
PlaceContext__Qemu__UseKvm=true
PlaceContext__Qemu__Executor=model
PlaceContext__Qemu__Agent__Provider=chat-completions
PlaceContext__Qemu__Agent__Endpoint=http://your-model-server:8080/v1/chat/completions
PlaceContext__Qemu__Agent__Model=your-vision-tool-model
PlaceContext__Qemu__Agent__ApiKey=optional-provider-key
PlaceContext__Qemu__Agent__MaxSteps=20
```

The endpoint is a full chat-completions URL using the function-tool and image-input protocol,
for example a compatible [LocalAI endpoint](https://localai.io/docs/basics/try/). Choose a model
that supports vision and tool calls. No provider/model is selected automatically. Keep API keys
in host configuration or a secret store; they are never copied into the guest. The host sends
the task prompt, workflow context, live desktop frames, and bounded command results to the chosen model.
The VM network toggle controls guest traffic; model calls use the host's connection.

The agent executes one tool at a time, with a maximum of 1–100 model turns (20 by default).
Guest commands have a 30-second limit; GUI programs should be started in the background with
output redirected. The workflow timeout includes boot, tools, and model calls. A model that
returns prose without a completion tool does not make the workflow succeed. Commands cannot
access a host shell, host-mounted files, or the model credential. A task can still interact with
external systems if guest networking is enabled and the prompt requests it.

The final summary is returned in `stdout`, failures in `stderr`, plus `exitCode`, `agentSteps`,
and an `activity` list of tool descriptions and exit codes. Model reasoning and live-frame bytes
are not stored in the workflow output. Context supplied to the model is limited to 65,536
characters. With the HTTP provider, the current Live preview frame replaces earlier images in the model context.

### Test using a local Codex login

Install Codex CLI on the workflow host and run `codex login` as the account running PlaceContext.
Check `codex login status`, then select the local provider instead of the HTTP settings:

```text
PlaceContext__Qemu__Agent__Provider=codex
PlaceContext__Qemu__Agent__Model=gpt-6-astra
```

This starts a private `codex app-server` process over stdio for each VM task and uses its existing
login. No API key or HTTP endpoint is required in PlaceContext. The process and VM are local;
model inference is remote and uses that account's access and usage allowance. Choose a model
available to the signed-in account. The integration was tested with Codex CLI 0.153.1 and uses
its experimental [dynamic-tool API](https://learn.chatgpt.com/docs/app-server).

Codex receives only the guest tools exposed by the runner. Host shell, browser/computer tools,
plugins, hooks, configured MCP servers, and host skill discovery are disabled for these sessions.
The thread is ephemeral, uses an empty temporary working directory with a read-only sandbox,
and cannot grant host-operation approvals. Live preview observations stay in its bounded task
history until teardown. Each tool request counts against `MaxSteps`; cancellation or the VM
deadline stops the app-server process and its children as well as the VM.

Run the opt-in test using a real model (this consumes account usage):

```bash
PLACECONTEXT_TEST_QEMU_IMAGE=/absolute/path/browser-linux.qcow2 \
PLACECONTEXT_TEST_QEMU_KVM=true \
PLACECONTEXT_TEST_CODEX_MODEL=gpt-6-astra \
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~CodexQemuIntegrationTests
```

The test asks Codex to create and open an offline HTML page, inspect its Live preview, and return
the unique page title. It independently checks the non-root Chromium window before teardown.
Normal test runs skip this check unless both image and model environment variables are set.

The persisted field is still named `command` for workflow/export compatibility, but its meaning
is now a prompt. Review older QEMU steps containing shell scripts and rewrite them as tasks;
there is no raw-script fallback. The editor reports a missing agent configuration before execution.

UseKvm requires access to `/dev/kvm`; leave it false for software emulation. The workflow editor
lists configured images and reports unavailable profiles. QEMU executes on the host running the
workflow, not inside a Kubernetes job pod. Its network toggle enables QEMU user networking; job
VPC/security-group assignments do not apply to QEMU steps.

Run the real desktop regression check after creating an image:

```bash
PLACECONTEXT_TEST_QEMU_IMAGE=/absolute/path/browser-linux.qcow2 \
PLACECONTEXT_TEST_QEMU_KVM=true \
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~QemuDesktopIntegrationTests
```

The checks open a local HTML page in Chromium, verify the non-root credentials, then check
fresh disks, timeout, cancellation, and temporary-file cleanup. A separate real-VM check drives
the production agent loop with scripted model responses, verifying guest actions, live-frame
feedback, and final completion. It does not validate the quality of an actual model. It does not disable Chromium's
sandbox. The VM has networking off during this test.

## Interactive login and reusable browser sessions

For the Jev executor, enable **Pause for login before starting** in the QEMU step, run the
workflow, and open its run details. The **Live preview** opens automatically as the main view,
with keyboard and mouse input locked while automation runs. **Take control** waits for a safe
agent checkpoint before unlocking input. Complete login/MFA there; never put credentials into
the task prompt or workflow input. **Resume automation** locks input again and makes the agent
observe the current page. Recorded actions remain available below the live preview.
The agent can also request human help when it encounters a login or access challenge.

While you have control, model requests and replay capture stop. Click **Save login** after login
finishes to save an encrypted Chromium profile. Saving briefly closes and reopens Chromium so
cookies and storage are flushed consistently. The profile is private to its authenticated creator,
project and workspace, and can only be restored with the same image profile. Select it in the
QEMU step's **Saved browser session** field for later runs. Every run gets a separate copy; disk
changes and refreshed sessions are not saved automatically. **Forget selected login** removes the
stored copy. Sessions expire according to the platform's rules and may need a fresh human login.
Device-bound credentials and external OS keyrings may not survive a new VM.

Profiles are limited to 8 MiB compressed, 128 MiB expanded, and 20 saved sessions per user/project.
Cache files and Chromium process locks are excluded. Credentials are not exported in workflow
manifests: an exported workflow that used a saved login instead pauses for login when imported.
Scheduled runs must execute as the profile's owner to use it.

The live console uses vendored noVNC 1.7.0, loaded only for an active desktop view. QEMU listens
on a private Unix socket in the run directory. The application checks cookie authentication,
workflow-run permission, workspace, project, owner and same-origin WebSocket requests before
proxying that socket. Preview connections reject keyboard and pointer input on the server and
discard clipboard messages. Resume revokes the interactive connection and reconnects the preview;
timeout and cancellation close all console connections. The
VM timeout includes time spent under human control; closing a tab does not resume the agent.

This first version keeps live console ownership on the execution host. Portal requests must reach
the same process that runs the VM; multi-host deployments need execution-host routing before
using live consoles across workers. It retains the existing one-VM-per-host capacity limit.

Verify the complete login/save/restore flow against a local fixture (no real credentials or paid
model calls):

```bash
PLACECONTEXT_TEST_QEMU_IMAGE=/absolute/path/browser-linux.qcow2 \
PLACECONTEXT_TEST_QEMU_KVM=true \
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~QemuInteractiveIntegrationTests
```

### Repeatable local startup

From the repository, run `./deploy/qemu/run-local.sh` after starting the local database.
It uses `dist/qemu/browser-linux.qcow2` (override with `PLACECONTEXT_QEMU_IMAGE`), detects
KVM access, and enables the Jev executor with the signed-in Codex vision model. Jev reads `TYPESAFE_API_KEY` from the current project’s vault first, falling back to host
configuration or the launching environment when no project entry exists. Existing `PlaceContext__Qemu__Agent__*`
values take precedence. Kubernetes defaults to `$HOME/.kube/config`. The portal binds to
`http://localhost:7700`. These local settings do not enable QEMU on production hosts.

Jev credentials resolve per project: the vault entry `TYPESAFE_API_KEY` takes precedence over
`PlaceContext:TypeSafe:ApiKey` and the host environment. This applies to browser automation,
workflow reviews and dashboard analysis. No credential is copied to another project or into
the VM. An invalid or unreadable vault entry fails instead of silently using the host key.
