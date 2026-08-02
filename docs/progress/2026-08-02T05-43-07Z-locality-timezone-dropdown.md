# Progress — Locality timezone dropdown

Timestamp: 2026-08-02T05:43:07Z

## Completed

- Replaced the editable timezone autocomplete in Settings → Locality with a true select dropdown.
- Populated the dropdown from the runtime's complete timezone database rather than maintaining a partial hard-coded list.
- Kept the existing workspace-timezone persistence and preview behavior unchanged.

## Verification

- Added a failing test before implementation for the timezone option source.
- Verified options are sorted, include UTC and Australia/Brisbane, and resolve through `TimeZoneInfo`.
- The focused test passes.
- `PlaceContext.Host` builds with zero warnings and zero errors.
