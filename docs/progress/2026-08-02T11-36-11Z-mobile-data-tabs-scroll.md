# Horizontally scrollable mobile Data tabs

**Completed:** 2026-08-02 21:36 AEST

## Change

The shared Data sub-navigation now preserves each tab's intrinsic width and scrolls horizontally below 700px. Touch momentum, contained horizontal overscroll, and a thin scrollbar keep all five destinations reachable without compressing labels.

## Verification

- Added a responsive CSS regression contract.
- Focused contract passed.
- Full `PlaceContext.Host.Tests` suite passed: 115 tests, 0 failures.
- Browser rendering at a 390px viewport confirmed the tab strip had a 342px client width, 376px scroll width, `overflow-x: auto`, thin scrollbar styling, and `flex: 0 0 auto` tab buttons.
