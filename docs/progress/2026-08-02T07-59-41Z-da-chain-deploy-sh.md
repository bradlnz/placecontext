# Development-application chain deployment through `deploy.sh`

**Timestamp:** 2026-08-02T07:59:41Z  
**Repository:** `devcontext`

## Objective

Make the production deployment path deploy the council-aware Queensland development-application preparation job chain together with PlaceContext, rather than requiring an ad hoc database command.

## Change

`deploy.sh` now:

1. Resolves the DA job source root from `DA_JOBS_ROOT`, defaulting to `~/code/ossen-reports/placecontext_jobs`.
2. Fails before deployment when any required job, council registry, overlay, or council-requirements source is missing.
3. Streams the minimum reviewed source bundle to the PlaceContext Kubernetes host after the application rollout.
4. Runs the committed idempotent `deploy_da_application.py` deployment there. The script updates job definitions through the management API and transactionally upserts the `development-application-preparation` chain in PostgreSQL.

The deployed sequential chain is:

1. `da-intake`
2. `resolve-site`
3. `council-registry`
4. `overlays`
5. `da-pathway`
6. `da-readiness`

The chain prepares and validates applications but retains mandatory qualified-planner and human lodgement review. It does not claim unattended portal submission.

## Verification

- `bash -n deploy.sh`: passed.
- `git diff --check -- deploy.sh`: passed.
- DA implementation and deployment unit tests: 20 passed.
- Live PostgreSQL inspection before this change confirmed the named chain and its three new jobs are present; the next deployment run will exercise the canonical `deploy.sh` path and verify the final production state again.
