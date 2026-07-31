\set ON_ERROR_STOP on

-- One executable contract test for every Job in the Ossen project.
--
-- Offline Jobs are enabled so the Tests page can run them as a suite.
-- Networked Jobs and Jobs that can send messages, mutate commerce data, or
-- consume paid media services are created as manual integration tests.
--
-- The stable test ID makes this seed repeatable. Reapplying it updates the
-- definition and resets the result only when the definition changed.
WITH job_contracts AS (
    SELECT
        j."Id" AS job_id,
        j."TenantId" AS tenant_id,
        j."ProjectId" AS project_id,
        j."Name" AS job_name,
        NOT (
            j."AllowNetworkEgress"
            OR j."Name" IN (
                'email-report',
                'generate-report',
                'report-shop-sync',
                'shopify-deliver',
                'shopify-order-sync',
                'slack-status',
                'suburb-backtest',
                'suburb-rank',
                'suburb-report'
            )
        ) AS enabled,
        CASE j."Name"
            WHEN 'cashflow-model' THEN
                '{"client_inputs":{"price":800000,"suburb":"Boondall","lga":"BCC","address":"Contract test","rent_per_room":470}}'
            WHEN 'envelope-validate' THEN
                '{"site":{}}'
            WHEN 'listing-scraper' THEN '{"suburbs":"Boondall"}'
            WHEN 'market-data' THEN '{"suburbs":"Boondall"}'
            WHEN 'product-comparison' THEN
                '{"price":800000,"land_m2":1000,"suburb":"Boondall","lga":"BCC","rent_per_room":470,"house_rent":750}'
            WHEN 'report-qa' THEN
                '{"markdown":"# Contract test report\n\nComparable sales — 20 comps\n\n## Cashflow & Funding\n\nPeak debt drawn\n","filename":"contract-test.md","audience":"internal","suburb":"Boondall"}'
            WHEN 'site-basis-validate' THEN
                '{"site":{"address":"Validation example","resolved_lotplan":"1RP1","parcel_lotplan":"1RP1","land_m2":1000,"analysis_site":{"area_m2":1000,"basis":"current_title","boundary_verified":true},"current_title":{"area_m2":1000,"lotplan":"1RP1","parcel_type":"Lot"}},"aerial":{"lotplan":"1RP1","lot_area_m2":1000,"parcel_type":"Lot"}}'
            WHEN 'site-accuracy-checklist' THEN
                '{"site":{"address":"Checklist example","lga":"BCC","resolved_lotplan":"1RP1","parcel_lotplan":"1RP1","analysis_site":{"area_m2":1000}},"aerial":{"lotplan":"1RP1","lot_area_m2":1000,"parcel_type":"Lot Type Parcel"},"validation":{"site_basis":{"passed":false}}}'
            WHEN 'site-plan-cad-vectorize' THEN
                '{"source_file":"","pdf_page":"1","extraction_mode":"auto","drawing_scale":"","known_distance_m":"","known_distance_pixels":"","min_line_length":"30"}'
            WHEN 'site-plan-mockup-higgsfield' THEN
                '{"source_file":"","address":"128 Stannard Road, Manly West","revision_brief":"Keep the site layout recognisable, give every dwelling a side-by-side double garage, and provide more clearly marked visitor parking.","dwelling_count":"18","visitor_spaces":"10","pdf_page":"1","aspect_ratio":"21:9","resolution":"4k"}'
            WHEN 'valuations' THEN
                '{"suburb":"Boondall","rooms":5,"cap_rate":0.07,"project":true}'
            ELSE '{}'
        END AS input_payload,
        md5(j."Id"::text || ':default-job-contract') AS stable_hash
    FROM jobs j
    WHERE j."ProjectId" = '6525de7d-be5d-427d-ba87-46b7154e430c'
),
definitions AS (
    SELECT
        (
            substring(stable_hash, 1, 8) || '-' ||
            substring(stable_hash, 9, 4) || '-' ||
            substring(stable_hash, 13, 4) || '-' ||
            substring(stable_hash, 17, 4) || '-' ||
            substring(stable_hash, 21, 12)
        )::uuid AS test_id,
        *,
        CASE
            WHEN enabled THEN 'Smoke contract'
            ELSE 'Integration contract · manual'
        END AS test_name,
        jsonb_build_array(
            jsonb_build_object(
                'Path', 'test.py',
                'Content', $validator$
import json
import sys

# Mock-only contract v2: the platform supplies no network, secrets, or Job side effects.
case = json.load(sys.stdin)
run = case.get("run") or {}

if run.get("status") != "Succeeded":
    raise AssertionError(f'Expected Succeeded, got {run.get("status")}')

shards = run.get("shards") or []
if not shards:
    raise AssertionError("Expected at least one completed shard")

failed = [
    shard for shard in shards
    if shard.get("exitCode") != 0 or shard.get("outcome") != "Succeeded"
]
if failed:
    raise AssertionError(f"{len(failed)} shard(s) did not succeed: {failed}")

output = run.get("output")
output_kind = "null" if output is None else type(output).__name__
print(f"{len(shards)} shard(s) passed; primary output type: {output_kind}")
$validator$
            )
        )::text AS code_files_json
    FROM job_contracts
)
INSERT INTO job_test_cases (
    "Id",
    "TenantId",
    "ProjectId",
    "JobId",
    "Name",
    "InputPayload",
    "AssertionType",
    "ExpectedValue",
    "Enabled",
    "LastStatus",
    "LastMessage",
    "LastActualOutput",
    "LastJobRunId",
    "LastRunAt",
    "LastDurationMs",
    "CreatedAt",
    "UpdatedAt",
    "RuntimeId",
    "Entrypoint",
    "CodeFilesJson",
    "AllowNetworkEgress"
)
SELECT
    test_id,
    tenant_id,
    project_id,
    job_id,
    test_name,
    input_payload,
    'Succeeds',
    NULL,
    enabled,
    'NotRun',
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    now(),
    now(),
    'python',
    'test.py',
    code_files_json,
    false
FROM definitions
ON CONFLICT ("Id") DO UPDATE SET
    "TenantId" = EXCLUDED."TenantId",
    "ProjectId" = EXCLUDED."ProjectId",
    "JobId" = EXCLUDED."JobId",
    "Name" = EXCLUDED."Name",
    "InputPayload" = EXCLUDED."InputPayload",
    "AssertionType" = EXCLUDED."AssertionType",
    "ExpectedValue" = EXCLUDED."ExpectedValue",
    "Enabled" = EXCLUDED."Enabled",
    "RuntimeId" = EXCLUDED."RuntimeId",
    "Entrypoint" = EXCLUDED."Entrypoint",
    "CodeFilesJson" = EXCLUDED."CodeFilesJson",
    "AllowNetworkEgress" = EXCLUDED."AllowNetworkEgress",
    "LastStatus" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN 'NotRun'
        ELSE job_test_cases."LastStatus"
    END,
    "LastMessage" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN NULL
        ELSE job_test_cases."LastMessage"
    END,
    "LastActualOutput" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN NULL
        ELSE job_test_cases."LastActualOutput"
    END,
    "LastJobRunId" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN NULL
        ELSE job_test_cases."LastJobRunId"
    END,
    "LastRunAt" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN NULL
        ELSE job_test_cases."LastRunAt"
    END,
    "LastDurationMs" = CASE
        WHEN job_test_cases."Name" IS DISTINCT FROM EXCLUDED."Name"
          OR job_test_cases."InputPayload" IS DISTINCT FROM EXCLUDED."InputPayload"
          OR job_test_cases."AssertionType" IS DISTINCT FROM EXCLUDED."AssertionType"
          OR job_test_cases."ExpectedValue" IS DISTINCT FROM EXCLUDED."ExpectedValue"
          OR job_test_cases."Enabled" IS DISTINCT FROM EXCLUDED."Enabled"
          OR job_test_cases."RuntimeId" IS DISTINCT FROM EXCLUDED."RuntimeId"
          OR job_test_cases."Entrypoint" IS DISTINCT FROM EXCLUDED."Entrypoint"
          OR job_test_cases."CodeFilesJson" IS DISTINCT FROM EXCLUDED."CodeFilesJson"
          OR job_test_cases."AllowNetworkEgress" IS DISTINCT FROM EXCLUDED."AllowNetworkEgress"
        THEN NULL
        ELSE job_test_cases."LastDurationMs"
    END,
    "UpdatedAt" = now();

SELECT
    count(*) AS total_tests,
    count(*) FILTER (WHERE "Enabled") AS enabled_smoke_tests,
    count(*) FILTER (WHERE NOT "Enabled") AS manual_integration_tests
FROM job_test_cases
WHERE "ProjectId" = '6525de7d-be5d-427d-ba87-46b7154e430c';
