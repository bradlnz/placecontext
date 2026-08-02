# Use Wiki-style CRM sub-page navigation

**Completed:** 2026-08-02 22:21 AEST

## Change

- Replaced the horizontal CRM section tabs with the established Wiki left-navigation pattern.
- Kept customer-operation context persistent while moving between Clients, Automations, and Settings.
- Added explicit selected-page semantics and retained section counts.
- Reused the Wiki mobile contract: a 44px section toggle, collapsed contents panel, current-section label, and 44px navigation targets.
- Kept the CRM workspace constrained with `min-width: 0` so dense content cannot force page overflow.

## Verification

- Added `Crm_uses_the_wiki_subpage_navigation_pattern` as a source-contract regression.
- Confirmed the focused regression failed before implementation and passed afterward.
- Full `PlaceContext.Host.Tests` suite: 119 passed, 0 failed.
- Browser-validated the desktop CRM hierarchy against a local compiled scoped stylesheet.
- Browser-validated a 390px mobile layout: no document overflow, 44px toggle, collapsed-by-default navigation, and 44px open navigation targets.

## Design references

- Existing PlaceContext Wiki navigation for sub-page hierarchy and responsive behavior.
- ClickHouse and Databricks patterns for persistent context, compact hierarchy, restrained status, and list-to-detail workflows.
