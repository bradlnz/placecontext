{{header}}

Create a reusable **{{AgentName}}** skill named `{{skillName}}` for this project.

Intent: {{intent}}

{{Instructions}}

Make the skill obey the project's requirements, reuse its established tooling/commands, and bake
in the PlaceContext workflow:
- **Pre-action**: load the project overview (`get_project_overview`), note starting tokens.
- **Guardrails** every change must pass: rationale, checks, review, live verification.
- **Post-action**: `record_activity` (rationale/items/check deltas/guardrail flags) and `record_usage`
  (tokens spent on that change — cost per change).

## Requirements
{{requirements}}

Write the file at the path above; for a Claude Code skill you can also persist it with the `scaffold_skill` tool.