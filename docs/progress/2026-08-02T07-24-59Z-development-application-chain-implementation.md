# Queensland Development Application preparation chain — implementation checkpoint

**Recorded:** 2026-08-02T07:24:59Z
**Status:** Locally implemented, reconciled, tested, and committed; production deployment and live smoke validation remain pending.

## Delivered implementation

The canonical PlaceContext DA preparation chain is implemented in `/home/brad/code/ossen-reports/placecontext_jobs` as one council-aware, sequential workflow:

1. `da-intake`
2. `resolve-site`
3. `council-registry`
4. `overlays`
5. `da-pathway`
6. `da-readiness`

The workflow covers Brisbane, Gold Coast, Ipswich, Lockyer Valley, Logan, Moreton Bay, Noosa, Redland, Scenic Rim, Somerset, Sunshine Coast, and Toowoomba while failing closed where authority, planning controls, assessment manager, referral screening, source currency, or council requirements are unresolved.

## Current statutory baseline

Official Queensland sources were reverified on 2 August 2026 and recorded with authority, scope, version/effective metadata, URL, and retrieval timestamp. The release registry now includes:

- Planning Act 2016 — current reprint effective 27 April 2026.
- Planning Regulation 2017 — current reprint effective 1 July 2026.
- Development Assessment Rules version 3.0 — effective 18 July 2025.
- Current Queensland forms and templates page.
- State Assessment and Referral Agency material.
- State Development Assessment Provisions.

Deployment preflight checked 63 official URLs: 25 were directly reachable and 38 were protected official endpoints. No stale, missing, or unavailable URL passed the release gate.

## Evidence and safety behavior

- Confirmed prohibited development blocks application preparation.
- Form 1/Form 2 selection remains tied to declared development aspects.
- Assessment category claims require structured professional evidence.
- Assessment manager selection is independent and must be evidenced; council is not assumed to be the manager.
- State/SARA referral screening requires structured evidence against current instruments.
- Impact assessment and variation requests expose the current public-notification baseline and remain review-gated.
- Environmental-authority, social-impact, and community-benefit material becomes required when its statutory trigger is declared.
- Automated planning-layer gaps no longer crash DA chains for unsupported councils; they produce explicit manual-review planning controls.
- Readiness blocks unresolved planning controls, assessment category, assessment manager, referral screening, owner consent, forms, and required documents.
- No external lodgement is performed. Human review and explicit authorisation remain mandatory.

## Reconciliation

The 17:00 repository automation committed the canonical implementation as `53b390c` and also committed a separate, weaker duplicate DA chain as `1b18b02`. The duplicate had stale form URLs, duplicated council route data, lacked the seven-day source gate, and had no downstream references. It was removed without rewriting history by revert commit `5cdc57e`.

## Verification

`python3 scripts/run_unit_tests.py` completed successfully from `placecontext_jobs`:

- All 27 PlaceContext test modules passed.
- DA preparation suite: 14 tests passed.
- DA deployment suite: 5 tests passed.
- Council/overlay regression suite: 5 tests passed.
- Expected skips were dependency-specific and unrelated to the DA chain.

Focused implementation commits:

- `9661a4b` — Harden Queensland DA evidence gates.
- `9328d13` — Deploy current DA chain dependencies.

## Remaining acceptance work

1. Run the canonical deployment script through the established PlaceContext production path.
2. Verify the three new jobs are registered and enabled and the two changed shared jobs match committed content.
3. Verify the `development-application-preparation` chain row and exact sequential stage IDs.
4. Execute representative metropolitan, regional, referral/overlay-trigger, and unresolved/source-gap live runs.
5. Confirm source citations/freshness and absence of an unsafe Brisbane fallback in live output.
6. Confirm every live pack remains blocked from external lodgement pending human review and explicit authorisation.

The earlier document `2026-08-02T05-46-27Z-development-application-chain-discovery.md` is superseded historical/comparable-DA design background and is not the delivered workflow.
