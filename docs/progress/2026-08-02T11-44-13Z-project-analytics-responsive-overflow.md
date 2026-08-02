# Fix Project Analytics responsive overflow

**Completed:** 2026-08-02 21:44 AEST

## Root cause

Unlike the Analytics view under Entities, the project-level Analytics page had no responsive breakpoint. Its 480px minimum chart columns, non-wrapping headers, and desktop padding forced content wider than mobile and tablet viewports.

## Change

- Matched the Entities Analytics breakpoint and compact page padding below 950px.
- Collapsed both chart grids to one flexible column.
- Allowed page, chart, editor, and redraw controls to wrap.
- Added `min-width: 0` containment and ellipsis for long chart names.
- Reduced mobile chart height to the same 220px used by Entities Analytics.
- Aligned desktop chart minimum widths with Entities Analytics at 440px.

## Verification

- Added a regression contract comparing the key responsive behaviors.
- Focused contract passed.
- Full `PlaceContext.Host.Tests` suite passed: 117 tests, 0 failures.
- Browser rendering at 390px confirmed both the body and page had matching 390px client/scroll widths, a single 362px chart column, wrapped headers/actions, 16px × 14px compact padding, and a 220px chart region.
