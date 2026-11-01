using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>
/// First-class executable contract gate for the job/data platform.
/// Validation runs in the same sandbox as every other code job.
/// </summary>
internal static class DataQualityJobTemplate
{
    /// <summary>
    /// Pure, dependency-free quality gate for JSON records passed between jobs.
    /// Rejects invalid batches with a nonzero exit and a structured evidence receipt.
    /// The next workflow step only runs when this job succeeds.
    /// </summary>
    public static JobTemplate Create() => new(
        Id: "json-data-quality",
        Name: "Validate data contract",
        Category: "Data quality",
        Description: "Check required columns, field types, row counts, null rates and unique IDs before loading or acting on records.",
        Icon: "test",
        MapSourceKind: "code",
        MapRuntimeId: "python",
        MapEntrypoint: "main.py",
        MapSource: Source,
        MapEnvRaw: "MIN_ROWS=1\nREQUIRED_FIELDS=id\nUNIQUE_FIELD=id\nMAX_NULL_RATE=0\nFIELD_TYPES_JSON={\"id\":\"string\"}",
        InputPayloadsRaw: "{\"records\":[{\"id\":\"demo-001\",\"name\":\"Example\"}]}",
        Parameters: Array.Empty<JobParameterDto>(),
        ReturnType: JobReturnType.Json,
        AllowNetworkEgress: false,
        RequiredCredentials: Array.Empty<JobCredentialRequirement>())
    {
        SourcesByRuntime = new Dictionary<string, string> { ["python"] = Source }
    };

    // Executed inside the normal Python job sandbox. All validation rules come from
    // the job's configuration, not untrusted incoming records.
    private const string Source = @"""""""Validate stdin JSON records against an explicit data contract, before downstream actions.""""""
import json
import os
import sys

MAX_INPUT_BYTES = 1024 * 1024
MAX_RECORDS = 5000
errors = []

def fail(message):
    errors.append(message)

def text_list(name):
    return [value.strip() for value in os.getenv(name, """").split("","") if value.strip()]

def number(name, fallback, low, high):
    raw = os.getenv(name, str(fallback))
    try:
        parsed = float(raw)
        if not (low <= parsed <= high):
            raise ValueError()
        return parsed
    except ValueError:
        fail(f""{name} must be between {low} and {high}"")
        return fallback

def dotted(record, name):
    value = record
    for segment in name.split("".""):
        if not isinstance(value, dict) or segment not in value:
            return None
        value = value[segment]
    return value

def expected(value, type_name):
    if type_name == ""string"":
        return isinstance(value, str)
    if type_name == ""number"":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if type_name == ""boolean"":
        return isinstance(value, bool)
    if type_name == ""object"":
        return isinstance(value, dict)
    if type_name == ""array"":
        return isinstance(value, list)
    return False

raw = sys.stdin.buffer.read(MAX_INPUT_BYTES + 1)
if len(raw) > MAX_INPUT_BYTES:
    fail(""Input exceeds 1 MiB; split the data into smaller batches"")
    payload = None
else:
    try:
        payload = json.loads(raw)
    except (ValueError, UnicodeDecodeError):
        payload = None
        fail(""Input must be valid UTF-8 JSON"")

records = payload if isinstance(payload, list) else payload.get(""records"") if isinstance(payload, dict) else None
if not isinstance(records, list):
    fail(""Expected an array or an object with a records array"")
    records = []
if len(records) > MAX_RECORDS:
    fail(""More than 5,000 records; partition before validating"")
min_rows = int(number(""MIN_ROWS"", 1, 0, MAX_RECORDS))
max_null_rate = number(""MAX_NULL_RATE"", 0, 0, 1)
required = text_list(""REQUIRED_FIELDS"")
unique_field = os.getenv(""UNIQUE_FIELD"", """").strip()
try:
    field_types = json.loads(os.getenv(""FIELD_TYPES_JSON"", ""{}""))
    if not isinstance(field_types, dict) or any(not isinstance(k, str) or t not in (""string"", ""number"", ""boolean"", ""object"", ""array"") for k, t in field_types.items()):
        raise ValueError()
except ValueError:
    field_types = {}
    fail(""FIELD_TYPES_JSON must map field names to supported types"")

if len(records) < min_rows:
    fail(f""Expected at least {min_rows} rows, got {len(records)}"")
if not required and not unique_field and not field_types and min_rows == 0:
    fail(""Configure at least one contract rule"")

missing = {field: 0 for field in required}
seen = set()
duplicates = 0
for index, record in enumerate(records[:MAX_RECORDS]):
    if not isinstance(record, dict):
        if len(errors) < 12:
            fail(f""Row {index + 1} is not an object"")
        continue
    for field in required:
        value = dotted(record, field)
        if value is None or value == """":
            missing[field] += 1
    for field, type_name in field_types.items():
        value = dotted(record, field)
        if value is not None and not expected(value, type_name) and len(errors) < 12:
            fail(f""Row {index + 1}: {field} must be {type_name}"")
    if unique_field:
        key = dotted(record, unique_field)
        if key is None or key == """":
            if len(errors) < 12:
                fail(f""Row {index + 1}: missing unique key {unique_field}"")
        else:
            stable = json.dumps(key, sort_keys=True, separators=("","", "":""))
            if stable in seen:
                duplicates += 1
            seen.add(stable)
for field, count in missing.items():
    rate = count / len(records) if records else 1.0
    if rate > max_null_rate:
        fail(f""{field}: missing in {count} of {len(records)} rows ({rate:.1%}); allowed {max_null_rate:.1%}"")
if duplicates:
    fail(f""{duplicates} duplicate values in {unique_field}"")

passed = not errors
report = {""passed"": passed, ""rows"": len(records), ""requiredFields"": required,
          ""uniqueField"": unique_field or None, ""missingCounts"": missing,
          ""duplicates"": duplicates, ""issues"": errors[:12]}
# Preserve the records only on success, enabling the next job in the workflow
# to receive the same payload. Failed checks never propagate rejected records.
out = {""records"": records, ""quality"": report} if passed else {""quality"": report}
print(json.dumps(out, separators=("","", "":""), ensure_ascii=False))
sys.exit(0 if passed else 1)
";

}
