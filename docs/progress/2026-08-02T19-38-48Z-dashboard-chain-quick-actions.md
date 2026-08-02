# Add dashboard job-chain quick actions

**Completed:** 2026-08-03 05:38 AEST

## Change

- Replaced the redundant dashboard Submit job action with a compact Run a job chain section.
- Loads real chains from the selected project and exposes up to four contextual Run controls.
- Uses the established chain-run service.
- Prevents duplicate starts with disabled/loading state and reports success or failure accessibly.
- Retains full chain management through View chains and full job submission through the Jobs page.
- Added contained desktop and mobile layouts with 44px phone controls.

## Verification

- Added `DashboardQuickActionsContractTests`.
- Full Host suite: 126 passed, 0 failed.
- Desktop dashboard visually reviewed in the hydrated local application.
- Confirmed the dashboard has no Submit job action and presents the new chain quick-action region.
