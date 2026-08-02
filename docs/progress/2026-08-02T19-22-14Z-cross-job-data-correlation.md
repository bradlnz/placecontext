# Preserve and correlate outputs from different jobs

**Completed:** 2026-08-03 05:22 AEST

## Change

- Allowed multiple job or chain mappings to contribute complementary fields to the same system-owned project table.
- Removed the stale ingestion preflight that rejected new mapped columns even though the project-data store safely evolves read-only table schemas transactionally.
- Added queryable `source_kind`, `source_id`, and `mapping_id` lineage to every mapped row alongside `run_id` and `ingested_at`.
- Added `$` as an explicit mapping selector for the complete record or scalar result.
- Preserved plain-text job results as mappable scalar values when no JSON payload is present.
- Kept objects and arrays as JSON values in declared columns, avoiding uncontrolled schema expansion while retaining all returned data.
- Continued refreshing record links and semantic indexing after each successful ingest, so cross-table identity correlation and RAG use the newly ingested rows.

## Verification

- Added coverage proving that outputs from different jobs can land in the same target table with distinct source lineage.
- Added coverage for additive schema evolution and plain-text/scalar ingestion.
- Data mapping ingestion suite passed: 13 tests.
- Full Application suite passed: 449 tests, 0 failed.
