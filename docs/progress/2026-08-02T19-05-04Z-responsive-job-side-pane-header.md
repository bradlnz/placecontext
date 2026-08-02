# Make the job side-pane header responsive

**Completed:** 2026-08-03 05:05 AEST

## Change

- Added explicit responsive roles to the job editor title, Run action, and close control.
- On mobile, the title and close target share the first row while the Run action wraps to a full-width second row.
- Preserved a 44px minimum target height for both Run and close controls.

## Verification

- Added `JobsSidePaneResponsiveContractTests.Job_side_pane_header_actions_wrap_on_mobile`.
- Confirmed the regression failed before implementation and passed afterward.
- Full Host suite passed: 123 tests.
- Validated at a synthetic 390px viewport: no horizontal document or header overflow, Run was 360px × 44px, and close remained 44px high.
