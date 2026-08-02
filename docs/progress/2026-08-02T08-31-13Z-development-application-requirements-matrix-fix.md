# Queensland DA requirements matrix correction

**Recorded:** 2026-08-02T08:31:13Z
**Status:** Root cause fixed, tested, deployed, and verified in production.

## Reported defect

The user reported that the requirements matrix was missing from the development-application preparation output.

## Root cause

`da-readiness` populated `requirements_matrix` only from `da_pathway.document_requirements`. This was a supporting-document checklist rather than a complete DA requirements matrix. It omitted statutory forms, assessment pathway, planning controls, assessment manager, state referral, public notification, owner consent, source currency, council lodgement procedure, and fees. If no supporting-document triggers were resolved, the matrix was empty and the PDF correctly had no rows to render.

The PDF renderer also treated structured source objects as generic text and expected the legacy `category` field instead of the canonical `classification` field.

## Correction

`da-readiness` now emits explicit matrix rows covering:

- statutory forms;
- supporting documents and technical reports;
- assessment pathway and benchmarks;
- planning controls;
- assessment manager;
- state referral screening;
- public notification;
- owner's consent;
- source currency;
- council lodgement procedure; and
- council fees and payment requirements.

Each row preserves requirement, classification, applicability, status, responsible authority, source metadata, evidence, and limitations/human action. Unresolved domains remain visible as `unverified`, `review_required`, or `manual_review_required`; they are not omitted.

`da-pdf` now normalises those rows for presentation, renders authority/source/limitations visibly, humanises machine labels, and returns matrix row count and classifications in final runtime metadata.

## Verification

- Regression test proves the matrix remains non-empty when `document_requirements` is empty.
- PDF integration test proves the full readiness matrix is preserved through rendering.
- Focused DA preparation tests: 15 passed.
- ReportLab PDF tests: 4 passed.
- Full jobs suite: all 28 PlaceContext test modules passed.
- Real local pathway → readiness → PDF run produced 17 matrix rows across ten classifications.
- Generated PDF: valid PDF 1.4, A4, four pages, extractable text.
- Visual checks confirmed a visible matrix heading, repeated table headers, readable rows/source URLs, and no clipping, overlap, or unsupported glyphs.

## Production evidence

Implementation commit:

- `c2430e2 Fix DA requirements matrix coverage`

Deployment source preflight:

- 63 checked;
- 54 current/reachable;
- 9 protected official sources.

Live chain run:

- Chain run: `321743e8-8fbb-451c-bd9f-0689e2a3a0f9`
- Status: `Succeeded`
- All seven stages: `Succeeded`
- Readiness run: `d1ce8e82-8468-4707-a34a-3de8c4ef6b8f`
- PDF run: `a0eb6d5e-aa30-44e6-b387-699216050701`
- Production matrix rows: **19**
- Production matrix classifications: assessment manager, assessment pathway, council procedure, owner consent, planning control, public notification, source currency, state referral, statutory form, and supporting document.
- PDF artifact: `da-preparation-report.pdf`
- Content type: `application/pdf`
- Size: 12,107 bytes
- SHA-256: `ad970e2bff7324b71082e0a5e97372e6c54d000367d248cd292bc20137bf835a`
- `lodgement_performed`: `false`
- `human_authorisation_required`: `true`

The production artifact link is persisted against the PDF-stage run. The workflow remains preparation-only and stops before council submission.
