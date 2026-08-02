# Align Observability with the Jobs catalogue

**Completed:** 2026-08-02 22:10 AEST

## Change

- Reworked job history, chain history, and live traces into the same compact catalogue hierarchy used by the Jobs view.
- Added active-run summary counts and a success progress indicator.
- Replaced disconnected run cards with one bordered suite containing a contextual header and dense, scannable rows.
- Matched the Jobs page width, title hierarchy, spacing, hover treatment, and status emphasis.
- Added a mobile layout that collapses summaries to two columns and stacks run metadata without horizontal overflow.

## Verification

- Focused `Observability_uses_the_jobs_catalogue_visual_hierarchy` regression passed: 1/1.
- Complete `PlaceContext.Host.Tests` suite passed: 118/118.
- Local desktop visual inspection confirmed the intended Jobs-style hierarchy and density.
- A 390px browser contract check reported `scrollWidth = clientWidth = 390px`; summaries used two columns and run rows collapsed to two columns.

## Existing warnings

- The build continues to report the pre-existing nullable warning in `ChatViewModel.Formatting.cs` and the existing EF Core relational package-version conflict.
