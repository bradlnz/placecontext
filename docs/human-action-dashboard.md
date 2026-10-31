# Human-in-the-loop action inbox

Data profiles surface real paused workflow input checkpoints above indicators and charts. This is a first integration of the operational loop:

Measured evidence → Jev assessment → human checkpoint → explicit response → durable continuation → measured outcome.

## What works

- Pending Human in the loop / Yes–No checkpoints from the project's durable workflow runs show workflow name, instructions, time and a link to the complete run.
- Approve and Decline open an explicit confirmation dialog. Neither browsing nor Jev evaluation sends a human response.
- The backend checks project, chains.manage and jobs.run permissions, current status, request identity and field shape immediately before submitting.
- The existing database queue serialises submissions, records the authenticated actor and resumes the same run. The dashboard reloads after a successful response and reports failures without claiming success.
- Scores from Jev are evidence, never authorisation. A Jev step must be wired to an explicit Human in the loop branch for its outcome to appear as a pending action.
- The inbox scans the latest 12 runs from up to 50 workflows in the selected project. This is a bounded view, not an all-time audit or real-time push feed.

## Lead onboarding

A lead onboarding workflow checks mandatory details, calls Jev to assess uncertain evidence and branches to a human checkpoint if confidence is insufficient. The dashboard shows that paused run and its instructions. The human inspects the complete run and explicitly responds Yes or No. The responder is recorded and the run continues through its configured branch.

**No does not automatically cancel a workflow.** The workflow owner must configure a safe No branch or terminal step before any downstream external actions. Request-more-information currently uses the linked Workflows interface, not an invented submission action. Individual lead/customer profiles require stable identities and verified source joins; chart labels are not customer IDs.

## Further development

Build an entity-aware indicator ledger with stable IDs, source provenance, decision fingerprints, approval policies, responder roles, notifications and correlated post-action outcome measurements. This version does not yet offer a full approval-policy editor or automated routing of low Jev support to a human step.
