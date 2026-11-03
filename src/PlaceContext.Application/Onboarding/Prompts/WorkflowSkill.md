# PlaceContext workflow — {{name}}

## At session start
- Call `get_project_overview` to load what's known.

## Before every change (pre-action)
- Confirm the change is in scope of the requirements below.
- Note your current token usage so you can report the cost of this change afterward.

## Guardrails — every change must pass these
- **Rationale** — know and state *why*, not just what.
- **Checks** — add or adjust the relevant checks; a change with no verification activity is flagged.
- **Review** — run a review on non-trivial work.
- **Live verification** — exercise the work and observe the result before calling it done.

## After every change (post-action)
- `record_activity` — rationale, touched items, check deltas, and only the guardrail flags you actually met.
- `record_usage` — the input/output tokens spent on *this* change, so cost is tracked per change.
- `add_decision` — capture any durable direction you decided.

## Requirements
{{requirements}}