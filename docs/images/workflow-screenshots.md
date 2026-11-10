# Workflow screenshot provenance

Recaptured in dark mode on 11 November 2026 (Australia/Brisbane) from the running PlaceContext
Blazor application at source revision
[`bd3d870`](https://github.com/bradlnz/placecontext/commit/bd3d870699ec11d2e94590bb4b034e45b8f1bfb5).
The Host Release build completed with zero warnings and zero errors.

The captures use a local PostgreSQL 16 database and sample company records. Screens are browser
captures of the actual application; no mock pages, generated
UI imagery, or substituted run statuses were used.

| Image | What it shows |
| --- | --- |
| [Query data](workflow-query-data.png) | The first step's Monaco SQL editor with syntax highlighting, table columns, a three-row preview, and the output passed to the agent. |
| [Decision flow](workflow-decision-flow.png) | The complete five-stage canvas, starting with Query data, then agent review, human approval, and the two conditional query stages. |
| [Human approval](workflow-human-approval.png) | A real run paused after the query and external-agent task completed. |
| [Completed run](workflow-run-completed.png) | The approved run: the handoff query succeeded and the follow-up query was skipped. |

## Execution checks

Two runs were completed through the real workflow engine and the portal's response controls:

- **Yes:** `approved` was true; `handoff.row_count` was 2; no `held_accounts` output was produced.
- **No:** `approved` was false; `held_accounts.row_count` was 3; no `handoff` output was produced.
- Both runs read three input rows, finished with Succeeded status, and recorded the unselected query
  as Skipped. Query results were not truncated.
- A fresh Yes run was completed for the dark-mode approval and completed-run captures, with the
  same three input rows and two handoff rows.

## Render checks

- Switched to dark mode using the portal's theme control and verified the shell's theme attribute.
- Waited for Monaco's SQL model and editor to mount, then for at least three distinct computed token
  colours in the rendered SQL. The query screenshot shows Monaco, not its plain-text fallback.
- Waited for the real SQL preview to return the three sample accounts before capture.
- Captured the complete canvas at 2160 × 760 with horizontal scroll at zero and no horizontal overflow,
  keeping the initial query and both decision stages visible.
- Waited for the completed run's JSON model to contain the handoff output and verified its row count.
  Inspected every final image for layout and styling.

The capture browser served the application's pinned Monaco 0.52.2 assets from the unmodified npm
package through a local request cache because CDN access was unavailable in that browser environment.
The application code, Monaco assets, SQL results, and UI styling were not modified for the captures.

The agent step used External agent mode. A reviewer response based on the displayed query results
was supplied through the handoff form; no automatic model service was used for these captures.
The approval responses are demonstration inputs. No customer messages, CRM changes, container jobs,
or virtual machines were executed. Kubernetes workers and QEMU were not needed for the captured steps.
