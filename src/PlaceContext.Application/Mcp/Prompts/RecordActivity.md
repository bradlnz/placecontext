{{header}}

Record the change you just made through PlaceContext's `record_activity` tool, satisfying every
trust gate:

1. **Rationale** — explain *why*, not just what.
2. **Checks** — report checks added/changed; a change with no verification activity is flagged.
3. **Touched items & nodes** — list exactly what you changed (these are the scope of the record).
4. **Review** — set `architectureReviewerRun` only if you actually ran a review.
5. **Live verification** — set `liveVerified` only if you exercised the work and observed the result.

The change must also uphold the project's requirements:

## Requirements
{{requirements}}

Then call `record_activity` with an honest, specific `commitMessage`.