# Workflow Monitor

The workflow-first navigation has three working areas:

- **Build:** create or revise jobs and complete executable workflows.
- **Workflows:** configure stages, conditions, AI evidence verification, human-input checkpoints,
  scheduled execution and run history.
- **Monitor:** inspect persisted execution indicators and respond to genuine paused workflow inputs.

There is no separate Agent Store or Data Profiles product. Data connections, indicators and AI
verification support workflows; the existing analytics and storage tools remain for evidence.

## Measured indicators

The Monitor summary is calculated from actual persisted workflow runs in the selected project.
The initial implementation scans at most **50 workflow definitions** and **12 recent runs per
definition**. It displays the **60 most recent runs across that bounded sample**. Workflow count
shows all definitions returned to the project.

The displayed counts include pending actions, running/waiting runs and completed successful runs.
The completion percentage is the number of Succeeded runs divided by terminal statuses
(Succeeded, Failed, Partial, Cancelled) **in the displayed sample**. Running, Waiting and
AwaitingInput are excluded from that denominator. Percentages are not presented as enterprise-wide
SLAs or proof of complete history. Select a run to inspect its actual trace in Observability.
Refresh reloads current persisted snapshots; it never executes a workflow or calls an AI model.

## Human-in-the-loop

The action inbox shows input requests from **AwaitingInput** runs, including explicit Human in
the loop checkpoints and job parameter prompts. Agent-input requests are intentionally not
shown as approvals.

Human Yes/No steps with a single checkbox-backed response can be answered after an explicit
confirmation. The view model does not mutate status locally; it revalidates on the server against
the project's current run and request ID. The existing **IJobChainSubmissionQueue.ResumeAsync**
performs tenant and actor checks, serialises responses under a database lock, records SubmittedBy,
and schedules continuation of the same run.

The server requires `jobs.view` for monitoring; `chains.manage` **and** `jobs.run` are
additionally required to see sensitive checkpoint prompts and to submit responses. Missing
permissions are not circumvented by client-side buttons.

**A No response is not cancellation.** It resumes the workflow with the configured No branch,
which the human must inspect before submitting the response. Text and job-parameter prompts link
to the Workflows page rather than inventing a new submission protocol.

## AI and data

AI verification remains an internal step in the workflow engine. It can support conditional
branches and route low-confidence evidence to explicit human checkpoints, but a model probability
is **not approval**, and Monitor never fabricates review requests from chart snapshots.
Per-customer indicators need true customer IDs and verified joins; current Monitor displays
run-based indicators only. It does not claim business-outcome improvements without stored
before/after measurements.

## Follow-up work

A future iteration can add correlated outcome indicators per workflow version, approval roles,
live subscriptions, and complete-history materialised aggregates. Those are not implied by the
bounded sample used in this first operational Monitor experience.
