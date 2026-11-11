# Browser automation with human control

Add browser work to a workflow using a disposable QEMU desktop. A configured vision model proposes actions, verification checks inspect live frames, and a person can take control when the task needs help.

## Prepare the environment

QEMU execution is enabled by default, but a browser task needs a prepared desktop image and configured models before it can run. The installer does not download those images or configure providers.

Follow the [QEMU setup guide](https://github.com/bradlnz/placecontext/blob/main/deploy/qemu/README.md) to prepare a Debian-compatible x86-64 desktop image and configure the workflow-host process. Container installations need the image mounted at its configured container path.

## Give the task an outcome

Add a QEMU browser task with a starting URL, clear instructions, and the result to verify. For example, inspect an order and return the displayed order ID and shipping status. Configure guest network access only when the task needs it.

The browser agent works from desktop frames. The default executor uses a vision model for proposed actions and Jev verification for checks. Provider credentials stay on the host.

## Take control when needed

Use **Take control** for login, MFA, or assistance. **Save login** stores an encrypted private browser session; **Resume automation** returns control to the agent.

The task timeout includes human interaction. Cancellation or timeout stops the VM. Disks are disposable, and guest networking starts disabled unless enabled on the step.

## Review the evidence

The run can retain activity, decision traces, and replay frames. Use these to inspect the action and result before moving the workflow forward. An explicit human approval step can gate any subsequent action that requires authorization.

## Plan the execution host

QEMU runs in the workflow-host process environment, with one active VM per host. Live console traffic must reach that process. It is not a Kubernetes job pod and requires separate execution-host security planning.

See the [platform reference](https://github.com/bradlnz/placecontext/blob/main/docs/platform-reference.md#qemu-enabled-by-default) for configuration keys, software emulation, KVM access, and executor options.
