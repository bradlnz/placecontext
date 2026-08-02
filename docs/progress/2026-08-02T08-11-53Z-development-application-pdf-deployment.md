# Queensland DA preparation PDF stage — production deployment

**Recorded:** 2026-08-02T08:11:53Z
**Status:** Implemented, tested, deployed, and validated in the live PlaceContext chain.

## Delivered

Added `da-pdf` as the final, sequential stage in `development-application-preparation`.

The stage:

- consumes the fail-closed `da_application_pack` emitted by `da-readiness`;
- renders a native A4 PDF named `da-preparation-report.pdf`;
- presents the application summary, requirements matrix, blockers, warnings, outstanding actions, lodgement control, and authoritative sources;
- prominently labels the report `NOT A LODGEMENT`;
- preserves `lodgement_performed: false` and `human_authorisation_required: true`;
- has no network egress or council-submission behaviour.

## Verification

Local verification:

- `test_da_application_pdf.py`: 3 tests passed under ReportLab 4.2.5.
- `test_deploy_da_application.py`: 7 tests passed.
- `test_da_application_preparation.py`: 14 tests passed.
- `scripts/run_unit_tests.py`: all 28 PlaceContext test modules passed; dependency-specific pre-existing skips remained explicit.
- Generated PDF inspected with `pdfinfo`, `pdftotext`, and a rendered PNG.
- Result: valid PDF 1.4, A4, two pages, unencrypted, extractable text, readable tables, no clipping, overlaps, or unsupported-glyph boxes.

Production source preflight immediately before deployment:

- 63 official sources checked;
- 54 current/reachable;
- 9 protected official sources;
- 0 invalid or unavailable sources.

## Deployment evidence

Jobs repository commit:

- `5fbfc3f Add DA preparation PDF stage`

Live job:

- Name: `da-pdf`
- ID: `f8e36bae-c5b0-43e7-9957-ef20a863fab4`
- Runtime: Python
- Entrypoint: `main.py`
- Return type: `Pdf`
- Return file: `da-preparation-report.pdf`

Live chain:

- Name: `development-application-preparation`
- ID: `8b8c0c04-44cb-4f20-9a50-2eedba28c104`
- Updated at: `2026-08-02T08:05:48.879872+00:00`
- Seven strictly sequential stages:
  1. `da-intake`
  2. `resolve-site`
  3. `council-registry`
  4. `overlays`
  5. `da-pathway`
  6. `da-readiness`
  7. `da-pdf`

## Live end-to-end smoke

Scenario: Brisbane metropolitan MCU preparation for `20 Balfour Street, Darra QLD 4076`.

- Chain run: `41f73034-b376-4808-9b4e-473c9be63379`
- Status: `Succeeded`
- All seven stages: `Succeeded`
- PDF stage run: `6c1c0e51-7a8c-4554-bef9-03178d0440c6`
- Final output status: `pdf_generated_pending_human_review`
- PDF bytes: 7,565
- SHA-256: `f820d76e1a6109acd054261e156b12944052cc60ec14e6799d3753fe10f67f6c`
- `lodgement_performed`: `false`
- `human_authorisation_required`: `true`

Production persisted the artifact as `application/pdf` in the configured object store and linked it to the PDF-stage job run.

## Safety boundary

The PDF is a preparation report, not an approval, planning opinion, completed statutory form, or lodged development application. Unverified requirements remain visible, and submission remains blocked pending explicit human review and authorisation.
