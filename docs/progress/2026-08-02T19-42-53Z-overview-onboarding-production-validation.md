# Validate Project Overview and onboarding changes in production

**Validated:** 2026-08-03 05:42 AEST

## Deployment

- Deployed through the repository `deploy.sh` workflow.
- Registry digest: `sha256:b9b14d53478da7661cc28840c8fac970715259581d873cc76e7c0df48d8e2162`.
- Kubernetes deployment `placecontext` in namespace `placecontext` rolled out successfully.
- Pod `placecontext-7db68fc844-zgsbc` reported `READY=true` and the same immutable image digest.

## Production artifacts

- Production root returned HTTP 200.
- `PlaceContext.Host.styles.css` returned successfully with 355,812 bytes.
- The production stylesheet contains the Project Overview viewport-bounded project grid, dashboard chain quick actions, and responsive Wiki navigation contracts.
- The unauthenticated production navigation exposes Wiki and About and no longer exposes Onboarding.

## Local hydrated-route verification before deployment

- Project Overview had no page-level horizontal overflow at 390×844 or 320×844.
- `/onboarding` resolved to `/wiki/getting-started`.
- Full Host suite passed: 126 tests, 0 failed.
