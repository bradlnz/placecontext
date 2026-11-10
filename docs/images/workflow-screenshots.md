# Workflow screenshot provenance

Captured on 10 November 2026 from the running PlaceContext Blazor application at source revision
[`bd3d870`](https://github.com/bradlnz/placecontext/commit/bd3d870699ec11d2e94590bb4b034e45b8f1bfb5).
The Host Release build completed with zero warnings and zero errors.

The captures use a local PostgreSQL 16 database and the
[customer-onboarding example](../../examples/customer-onboarding/README.md). All company records
are sample data. Screens are browser captures of the actual application; no mock pages, generated
UI imagery, or substituted run statuses were used.

| Image | What it shows |
| --- | --- |
| [Query data](workflow-query-data.png) | The real query editor, table columns, a three-row SQL preview, and the output passed to the next step. |
| [Decision flow](workflow-decision-flow.png) | The editor scrolled to agent review, human approval, and the two conditional query stages. |
| [Human approval](workflow-human-approval.png) | A real run paused after the query and external-agent task completed. |
| [Completed run](workflow-run-completed.png) | The approved run: the handoff query succeeded and the follow-up query was skipped. |

## Execution checks

Two runs were completed through the real workflow engine and the portal's response controls:

- **Yes:** `approved` was true; `handoff.row_count` was 2; no `held_accounts` output was produced.
- **No:** `approved` was false; `held_accounts.row_count` was 3; no `handoff` output was produced.
- Both runs read three input rows, finished with Succeeded status, and recorded the unselected query
  as Skipped. Query results were not truncated.

The agent step used External agent mode. A reviewer response based on the displayed query results
was supplied through the handoff form; no automatic model service was used for these captures.
The approval responses are demonstration inputs. No customer messages, CRM changes, container jobs,
or virtual machines were executed. Kubernetes workers and QEMU were not needed for the captured steps.
