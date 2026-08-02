# Progress — Search documents open related artifacts

Timestamp: 2026-08-02T05:37:32Z

## Completed

- Updated workspace search result routing so OpenSearch documents carrying an `artifact_id` field open the related artifact directly.
- Artifact identifiers are recognized across nested field paths and common underscore, hyphen, and casing variants.
- Documents without an artifact relationship retain the existing deep link to Data Search.

## Verification

- Added a failing regression test before implementation and confirmed it failed against the previous behavior.
- Passed all 11 `SearchTests` after implementation.
- Built `PlaceContext.Host` successfully.
- The build retains one pre-existing nullable warning in `ChatViewModel.Formatting.cs`; this change introduces no build errors.
