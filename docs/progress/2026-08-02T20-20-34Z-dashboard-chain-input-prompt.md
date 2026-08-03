# Dashboard chain input prompt and panel spacing

**Completed:** 2026-08-02T20:20:34Z

## Summary

Updated the Dashboard **Run a job chain** quick action so it has the same inset panel treatment as the surrounding dashboard cards and never skips declared job inputs.

## Changes

- Applied balanced `16px` desktop and `14px` mobile padding to the full quick-action panel instead of padding only individual rows.
- Added a responsive, accessible run-input dialog for chains containing parameterized jobs.
- Reused the existing typed `ParamInput` controls, including file inputs and required-field validation.
- Prefilled prompts from each job's persisted input payloads.
- Passed validated values as flat execution-step payload overrides, preserving action-stage and parallel-job indexes.
- Kept chains without declared parameters on the direct quick-run path.
- Preserved cancellation, duplicate-run suppression, and success/failure feedback.
- Extracted `ChainParameterPromptPlan` so Dashboard and Job Chains use one tested parameter-step/indexing implementation.

## Verification

- Red-first Dashboard source contract failed before implementation and passed afterward.
- `ChainParameterPromptPlan_finds_parameterized_steps_and_prefills_stored_values` validates parameter discovery, execution indexes, persisted defaults, and exclusion of parameterless steps.
- Focused Dashboard/parameter tests: **9 passed**.
- Full `PlaceContext.Host.Tests`: **128 passed**.
- `git diff --check`: passed.
- Rebuilt Host started at `http://127.0.0.1:7710`.
- Browser validation confirmed balanced panel insets and no desktop clipping or overflow on the empty-project Dashboard state.

## Known existing build warnings

The suite still reports the existing EF Core Relational package-version conflict and the existing nullable warning in `ChatViewModel.Formatting.cs`; neither was introduced by this change.
