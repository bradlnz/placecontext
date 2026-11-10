terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

resource "placecontext_job" "nightly" {
  project_id  = "00000000-0000-0000-0000-000000000000" # a placecontext_project.id
  name        = "Nightly revenue rollup"
  description = "Summarises yesterday's orders."

  # Exactly one map workload: either an image, or a runtime plus source/files.
  map_image = "ghcr.io/acme/revenue:1.4.0"

  map_env = {
    TZ = "UTC"
  }

  input_payloads       = ["2026-01-01"]
  concurrency_limit    = 2
  success_exit_codes   = [0]
  partial_exit_codes   = [2]
  allow_network_egress = false
  retry_count          = 1
  retry_delay_seconds  = 30

  parameters = [
    {
      name     = "report_date"
      label    = "Report date"
      type     = "date"
      required = true
    },
  ]

  return_type      = "Table"
  post_job_actions = ["Csv"]
}

# A code-based job: a runtime plus source files instead of an image.
resource "placecontext_job" "validate" {
  project_id = "00000000-0000-0000-0000-000000000000"
  name       = "Validate order feed"

  map_runtime_id = "python3.12"
  map_entrypoint = "main.py"
  map_files = [
    {
      path    = "main.py"
      content = <<-PY
        import json, sys
        payload = json.load(sys.stdin)
        assert "orders" in payload
        print(json.dumps({"valid": True}))
      PY
    },
  ]

  return_type = "Json"
}
