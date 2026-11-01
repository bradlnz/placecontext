# Awaiting-input workflow notifications

The workflow engine already persists `AwaitingInput` for missing job parameters and human
checkpoints. The normal notifications bell previously treated every non-running chain status
as a finished operation, so paused workflows could look complete and never trigger a
persistent action request.

## Behaviour

- The existing run-status watcher now includes durable `AwaitingInput` and scheduled `Waiting`
  workflow runs in its read model, including workflows paused before the current process started.
  The watcher polls on the configured run-status interval (default 2 seconds).
- An `AwaitingInput` transition becomes a **Needs input** status on the existing correlation key
  `chain-run:<run id>`. Repeated sweeps update one bell item, not duplicate notifications.
- The bell highlights the number of workflows awaiting input, sorts actionable requests ahead of
  routine progress and offers **Review**. Only members with `chains.manage` and `jobs.run`
  permissions see the actionable badge and details.
- **Review** links to `/project/{projectId}/workflows?chain={chainId}&run={runId}`, so the
  Workflows page opens the actual paused run, not just the workflow definition.
- In **Workflows → Runs**, a visible attention banner and row marker emphasize the paused
  requests even when the notifications pane is closed.
- On submission, the existing input queue records the actor and transitions the workflow to
  `Waiting`. The watcher updates the **same** bell item, clears Needs input and eventually
  displays the terminal result. A No answer follows the explicitly configured branch and
  does not automatically cancel execution.
- The notification does not include the original input payload or request values, and does not
  call any model or execute any workflow. The normal server-side Workflows permissions remain
  the authority for opening and submitting the request.

## Limits

These are **in-app notifications**, not emails, push notifications or a persisted per-user unread
inbox. The active request is rebuilt from persisted workflow state after a replica restart.
The operation ledger is in-memory per replica; multiple replicas converge through the watcher.
The badge represents pending requests, not unread messages; it automatically disappears when
the workflow no longer requires input. No schema migration is needed.

Tests cover transitions, deduplication, same-run links, acknowledgement, tenant-scoped operation
updates and ensuring a stale in-process completion cannot hide an unresolved input request.
