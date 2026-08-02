# Development-application job-chain discovery

Timestamp: 2026-08-02T05:46:27Z

## Outcome

Create one council-aware `development-application-intelligence` chain. Do not create one chain per council and do not silently fall back to Brisbane. The chain must resolve the authoritative LGA first, apply the council's source/licensing policy, query only permitted DA records, and emit an explicit unsupported or review-required result where evidence is unavailable.

This discovery did not deploy or alter production jobs or chains.

## Existing capabilities

### PlaceContext

- Job chains support sequential stages, bounded parallel stages and JSON-payload gates.
- Production has no dedicated development-application chain.
- `full-feasibility-report` currently runs `resolve-site -> da-validation -> council-registry -> overlays` as part of a larger sequential pipeline.
- Existing relevant jobs:
  - `resolve-site`
  - `da-validation`
  - `council-registry`
  - `overlays`
  - `planning-rules-verify`
- The deployed feasibility chain remains sequential because its deployment script records a current host `DbContext` fan-out defect.
- `ConditionGate.ElseBranch` is represented in the domain/read model, but `RunJobChainHandler` currently skips a false-gated stage without executing the else branch. Council routing must therefore live in a council-aware job until else-branch execution is implemented and verified.

### Existing DA job

`da-validation`:

- queries `pi-development-applications-v1` for exact subject-property matches;
- classifies active, approved, historical and post-approval records;
- preserves unknown/unavailable state when OpenSearch cannot be queried;
- returns subject applications and document metadata;
- supplements the result with a bundled suburb-level precedent summary.

It does not yet provide a complete DA-intelligence chain because it:

- does not resolve and enforce council source policy before searching;
- does not filter the OpenSearch query by the resolved authority;
- does not build a fresh comparable-DA cohort from live OpenSearch records;
- does not rank comparables by planning/site similarity;
- does not extract conditions, information requests, infrastructure requirements, relaxations or refusal reasons;
- can expose source document metadata without a council-specific redistribution policy;
- uses a bundled suburb summary whose freshness is independent of the live index.

### Existing council job

`council-registry` is a planning-overlay API registry, not a DA-source registry. Its curated planning coverage currently includes Brisbane, Gold Coast, Ipswich, Logan, Moreton Bay and Redland. Sunshine Coast and Toowoomba have DA records but no curated planning-overlay registry entry. Lockyer Valley, Noosa, Scenic Rim and Somerset have neither an automated DA source nor a curated planning registry entry.

## Live SEQ DA coverage

Live OpenSearch authority aggregation was checked on the production search host.

| Council | Live DA records | DA-source position | Planning-overlay registry |
|---|---:|---|---|
| Brisbane | 141,163 | Accepted, official Development.i WFS, CC BY 4.0 | Curated |
| Gold Coast | 13,705 | Accepted with limitation: rolling 12 months, no source coordinates | Curated |
| Ipswich | 7,897 | Owner-directed restricted use; derived reports only | Curated, but zone source requires verification |
| Lockyer Valley | 0 | Not automated; no supported licensed feed | Missing |
| Logan | 27,337 | Accepted, official ArcGIS, CC BY 3.0 AU | Curated |
| Moreton Bay | 32,897 | Accepted with limitation: source records have no coordinates | Curated |
| Noosa | 0 | Automation prohibited without consent | Missing |
| Redland | 7,212 | Owner-directed restricted use; derived reports only | Curated |
| Scenic Rim | 0 | Commercial reuse prohibited/no supported feed | Missing |
| Somerset | 0 | No supported licensed feed | Missing |
| Sunshine Coast | 9,890 | Accepted, official ArcGIS, CC BY 3.0 AU | Missing |
| Toowoomba urban extent | 5,005 | Owner-directed restricted use; derived reports only | Missing |

The four zero-coverage councils must remain visible as unsupported/pending permission. They must not be scraped through browser-oriented trackers or represented as having no relevant development applications.

## Recommended chain

Name: `development-application-intelligence`

Input contract:

```json
{
  "address": "20 Balfour Street, Darra QLD 4076",
  "radius_m": 3000,
  "lookback_years": 5,
  "application_types": ["Reconfiguring a Lot"],
  "max_comparables": 20
}
```

Recommended sequential stages:

1. **`resolve-site`** (existing)
   - Resolve canonical address, coordinates, lot/plan, locality and authoritative LGA.
   - Fail closed when the LGA cannot be resolved; never default to BCC.

2. **`da-source-policy`** (new)
   - Read a versioned council DA-source catalogue separate from the planning-overlay registry.
   - Emit authority id/name, source ids, permitted use, attribution, date coverage, coordinate completeness, document-use policy and source status.
   - Outcomes: `supported`, `supported_with_limitations`, `restricted_derived_only`, `unsupported_pending_permission`, `unknown_council`.
   - An unsupported outcome remains a successful evidence result, not an empty DA result.

3. **`da-query`** (new or a cohesive extraction from `da-validation`)
   - Query the canonical OpenSearch index using the resolved authority plus subject identifiers.
   - Return exact subject matches and a bounded comparable candidate cohort.
   - Apply authority, source-active, date-window, application-type and jurisdiction filters before scoring.
   - Keep raw council records and documents out of restricted derived-report outputs.

4. **`council-registry`** (existing, corrected)
   - Resolve planning API coverage for the same authoritative LGA.
   - Remove the BCC default and return explicit `unsupported` when no curated planning entry exists.

5. **`overlays`** (existing)
   - Query current planning layers for the resolved subject coordinates when a curated registry exists.
   - For Sunshine Coast and Toowoomba, emit `review_required: planning_registry_missing` until curated sources are added.

6. **`planning-rules-verify`** (existing)
   - Validate that conclusions use current, cited council/state controls and that no silent numeric fallback was introduced.

7. **`da-comparable-rank`** (new)
   - Rank live candidates by council, zone/precinct, application type, site area, frontage, distance, retained building, proposed layout, access and mapped constraints.
   - Missing dimensions reduce confidence; they must not be imputed as matches.
   - Preserve score components and evidence provenance for auditability.

8. **`da-evidence-pack`** (new)
   - Produce a compact JSON and report artifact containing subject applications, ranked precedents, approval/refusal/condition evidence, source limitations, attribution and professional-review gates.
   - State that precedent is evidence and not a guaranteed approval outcome.
   - Never claim “no DA” when the source is unavailable, blocked or incomplete.

Keep the stages sequential for the first implementation. Parallelise subject and comparable queries only after the production fan-out/DbContext path is fixed and a join job preserves the chain envelope.

## Council routing policy

### Full DA + planning route

- Brisbane
- Logan
- Gold Coast, with coordinate/history limitations
- Moreton Bay, with coordinate limitation
- Ipswich, restricted derived-report output
- Redland, restricted derived-report output

### DA route with planning review gate

- Sunshine Coast
- Toowoomba urban extent, restricted derived-report output

### Unsupported/pending-permission route

- Lockyer Valley
- Noosa
- Scenic Rim
- Somerset

For unsupported councils, the chain should stop after producing a source-gap artifact that records the official tracker, legal/interface blocker and required permission or supported-feed remedy.

## Required implementation slices

1. Add the versioned DA source-policy catalogue and `da-source-policy` job with tests for all 12 councils.
2. Fix `council-registry` so missing LGA never becomes BCC, then add Sunshine Coast and Toowoomba planning-source discovery/curation as separate evidence-backed work.
3. Split live querying from bundled analytics: implement authority-filtered subject and comparable queries with deterministic tests.
4. Add comparable scoring and evidence-pack generation with provenance and restricted-source redaction tests.
5. Create the production chain through the normal PlaceContext management path; do not update `job_chains.StagesJson` ad hoc.
6. Run a Brisbane/Darra end-to-end test first, then one representative site in every supported council and one blocked-council test.
7. Deploy through `deploy.sh` and validate the portal and final artifact with Playwright.

Each slice should have its own timestamped progress note, focused tests and separate commit.

## Acceptance criteria

- All 12 councils produce an explicit, tested source-policy outcome.
- No unknown or unsupported council falls back to Brisbane.
- OpenSearch DA queries include the resolved authority/source constraint.
- Search failures and blocked sources produce `unknown`/`unsupported`, never `no_matching_da`.
- Restricted councils produce derived evidence only and do not redistribute raw records/documents.
- Gold Coast and Moreton coordinate limitations are visible in comparable confidence.
- Sunshine Coast and Toowoomba do not receive unsupported planning conclusions.
- Comparable scores expose their component evidence and missing-data penalties.
- Every planning or DA conclusion includes source id, authority, access/query time and relevant source limitation.
- The 20 Balfour Street, Darra reference case completes end to end.
- One test per supported council and one test per blocked council passes in production-safe smoke validation.
