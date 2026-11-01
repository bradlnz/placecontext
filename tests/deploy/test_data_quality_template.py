"""Executable checks for the exact Python source embedded in the .NET job template."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / "src/PlaceContext.Host/Components/ViewModels/Jobs/DataQualityJobTemplate.cs"

def source():
    text = CATALOG.read_text(encoding="utf-8")
    match = re.search(r'private const string Source = @"(.*?)";', text, re.S)
    assert match is not None
    # C# verbatim literals double every embedded quote.
    return match.group(1).replace('""', '"')

def run(records, **rules):
    env = {**os.environ, "MIN_ROWS": "1", "REQUIRED_FIELDS": "id",
           "UNIQUE_FIELD": "id", "MAX_NULL_RATE": "0",
           "FIELD_TYPES_JSON": '{"id":"string"}', **rules}
    output = subprocess.run([sys.executable, "-c", source()],
                            input=json.dumps(records), text=True,
                            capture_output=True, env=env, timeout=8)
    return output.returncode, json.loads(output.stdout)

def test_valid_rows_continue_with_original_payload():
    data = {"records": [{"id": "a", "name": "A"}, {"id": "b", "name": "B"}]}
    code, result = run(data)
    assert code == 0
    assert result["records"] == data["records"]
    assert result["quality"]["passed"] is True
    assert result["quality"]["rows"] == 2

def test_duplicate_ids_block_downstream_execution_without_output_records():
    code, result = run([{"id": "a"}, {"id": "a"}])
    assert code == 1
    assert result["quality"]["duplicates"] == 1
    assert "records" not in result

def test_required_fields_and_type_changes_are_detected():
    code, result = run({"records": [{"id": 123}, {"name": "missing"}]})
    assert code == 1
    assert result["quality"]["missingCounts"]["id"] == 1
    assert any("must be string" in issue for issue in result["quality"]["issues"])

def test_null_rate_policy_is_configured_in_job_not_in_untrusted_payload():
    payload = {"records": [{"id": "a"}, {"id": None}],
               "contract": {"MIN_ROWS": 0, "MAX_NULL_RATE": 1}}
    code, result = run(payload, UNIQUE_FIELD="", MAX_NULL_RATE="0.4")
    assert code == 1
    assert any("allowed 40.0%" in issue for issue in result["quality"]["issues"])

def test_invalid_configuration_fails_closed():
    code, result = run([{"id": "a"}], FIELD_TYPES_JSON='{"id":"secret"}')
    assert code == 1
    assert result["quality"]["passed"] is False
    assert "records" not in result

def test_empty_input_fails_closed():
    code, result = run([])
    assert code == 1
    assert result["quality"]["rows"] == 0
    assert "records" not in result
