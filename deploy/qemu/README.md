# QEMU desktop images

The workflow runner accepts prepared Debian-compatible x86-64 qcow2 desktop images and a
natural-language task prompt. An agent observes screenshots, uses Chromium/xdotool and guest
commands, checks results, and returns a final summary or failure. Commands run as `workflow`,
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

The builder installs X11, Openbox, Chromium, xdotool, scrot, and the QEMU guest agent. It selects Debian's standard
kernel because the cloud kernel omits the virtio graphics driver. It creates a password-locked
`workflow` user without sudo access and enables desktop autologin. Provisioning uses outbound
internet access to Debian's package repositories. It uses KVM when available, otherwise software
emulation, and stops after 30 minutes. Progress is written to `browser-linux.qcow2.build.log`.
The output image is flattened and never overwrites an existing image.

The base cloud image is trusted input. Keep both it and the final image writable only by the
operator, and run the application itself as a non-root account. Do not put credentials into the
image. The build image has no SSH login credentials and disables root/password SSH login.

## Configure and test

Set these on each host that executes workflows:

```text
PlaceContext__Qemu__Enabled=true
PlaceContext__Qemu__Images__browser-linux=/absolute/path/browser-linux.qcow2
PlaceContext__Qemu__UseKvm=true
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
the task prompt, workflow context, screenshots, and bounded command results to the chosen model.
The VM network toggle controls guest traffic; model calls use the host's connection.

The agent executes one tool at a time, with a maximum of 1–100 model turns (20 by default).
Guest commands have a 30-second limit; GUI programs should be started in the background with
output redirected. The workflow timeout includes boot, tools, and model calls. A model that
returns prose without a completion tool does not make the workflow succeed. Commands cannot
access a host shell, host-mounted files, or the model credential. A task can still interact with
external systems if guest networking is enabled and the prompt requests it.

The final summary is returned in `stdout`, failures in `stderr`, plus `exitCode`, `agentSteps`,
and an `activity` list of tool descriptions and exit codes. Model reasoning and screenshot bytes
are not stored in the workflow output. Context supplied to the model is limited to 65,536
characters. With the HTTP provider, the current screenshot replaces earlier images in the model context.

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
and cannot grant host-operation approvals. Screenshot observations stay in its bounded task
history until teardown. Each tool request counts against `MaxSteps`; cancellation or the VM
deadline stops the app-server process and its children as well as the VM.

Run the opt-in test using a real model (this consumes account usage):

```bash
PLACECONTEXT_TEST_QEMU_IMAGE=/absolute/path/browser-linux.qcow2 \
PLACECONTEXT_TEST_QEMU_KVM=true \
PLACECONTEXT_TEST_CODEX_MODEL=gpt-6-astra \
dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~CodexQemuIntegrationTests
```

The test asks Codex to create and open an offline HTML page, inspect its screenshot, and return
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
the production agent loop with scripted model responses, verifying guest actions, screenshot
feedback, and final completion. It does not validate the quality of an actual model. It does not disable Chromium's
sandbox. The VM has networking off during this test.
