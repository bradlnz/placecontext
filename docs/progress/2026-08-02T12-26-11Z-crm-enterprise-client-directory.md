# Redesign the CRM client directory as an enterprise list

**Completed:** 2026-08-02 22:26 AEST

## Change

- Replaced the desktop client card gallery with a compact, flat account directory.
- Added explicit Account, Contact, Lifecycle, and Open columns with consistent row-level actions.
- Kept account identity visually primary while company and contact metadata remain secondary.
- Preserved inline lifecycle updates and the existing client detail flow.
- Restricted lifecycle color to small avatar/status signals rather than decorative card surfaces.
- Added a dedicated mobile record-list treatment instead of squeezing desktop columns horizontally.
- Mobile rows prioritize identity, contact, lifecycle, and a 44px detail action.

## Verification

- Added `Crm_directory_uses_an_enterprise_list_to_detail_hierarchy` as a source-contract regression.
- Confirmed the focused regression failed before implementation and passed afterward.
- Full `PlaceContext.Host.Tests` suite: 120 passed, 0 failed.
- Browser-validated the desktop directory against the locally compiled scoped stylesheet.
- Browser-validated at 390px: no document overflow, desktop header hidden, record width contained to 366px, and 44×44px detail actions.

## Design references

- Databricks list → object → detail hierarchy and flat resource rows.
- yellow compact operational tables, restrained status signals, and contextual actions.
- Existing PlaceContext visual tokens, client detail workflow, and responsive shell contracts.
