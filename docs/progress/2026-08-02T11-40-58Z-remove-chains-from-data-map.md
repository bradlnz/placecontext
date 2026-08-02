# Remove chains from the Data map

**Completed:** 2026-08-02 21:41 AEST

## Change

- Removed chain nodes and chain-to-table connection interactions from the Data map canvas.
- Removed chain selection from the mapping editor; new Data map mappings are now explicitly job-to-table mappings.
- Stopped loading chain definitions for this page.
- Filtered existing chain mappings out of the visual Data map so historical backend records continue operating without presenting phantom source nodes or invalid edges.

## Verification

- Added a regression contract covering the page and all three Data map view-model partials.
- Focused regression passed.
- Full `PlaceContext.Host.Tests` suite passed: 116 tests, 0 failures.
- Host compiled successfully as part of both test runs.
