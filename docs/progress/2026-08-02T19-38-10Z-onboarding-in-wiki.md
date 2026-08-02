# Move onboarding into the Wiki

**Completed:** 2026-08-03 05:38 AEST

## Change

- Removed Onboarding from the built-in main-menu catalogue and menu settings catalogue.
- Expanded Wiki → Getting started with MCP connection, OAuth, first-project onboarding, and the operational working loop.
- Preserved `/onboarding` as an authorized compatibility route that redirects to `/wiki/getting-started`.
- Updated the empty project switcher to link directly to the getting-started guide.
- Removed the retired standalone onboarding stylesheet.

## Verification

- Added `OnboardingWikiNavigationContractTests`.
- Full Host suite: 126 passed, 0 failed.
- Browser route verification confirmed `/onboarding` resolves to `/wiki/getting-started`.
- Browser menu verification confirmed Onboarding is absent while Wiki remains discoverable.
- The rendered Getting started article includes the MCP endpoint, client command, OAuth flow, project onboarding, and data-platform workflow.
