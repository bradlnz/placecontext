# Wrap running chain steps on mobile

**Completed:** 2026-08-03 05:04 AEST

## Change

- Replaced the mobile chain pipeline's forced single-line layout with contained wrapping.
- Allowed stage columns to shrink and wrap while keeping long job names, provider values, external IDs, and errors inside their cards.
- Hid inter-stage arrows on wrapped mobile layouts so arrows cannot become orphaned at line boundaries.
- Bounded the Running steps panel to the viewport and allowed its header labels to wrap.

## Verification

- Added `ChainPipelineResponsiveContractTests.Running_steps_wrap_and_remain_contained_on_mobile`.
- Confirmed the regression failed before implementation and passed afterward.
- Full Host suite passed: 122 tests.
