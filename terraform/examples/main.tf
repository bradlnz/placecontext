terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

# Endpoint and api_key can also come from the PLACECONTEXT_ENDPOINT and
# PLACECONTEXT_API_KEY environment variables — omit them here to use those.
provider "placecontext" {
  endpoint = "http://acme.localhost:7700" # or https://acme.placecontext.ai
  api_key  = var.placecontext_api_key
}

variable "placecontext_api_key" {
  type      = string
  sensitive = true
}

# A project — registered idempotently by path. There is no delete path in the
# API, so `terraform destroy` only removes it from state.
resource "placecontext_project" "storefront" {
  path = "/home/brad/code/storefront-api"
  name = "storefront-api"
}

# An image-based job with no reduce step (the minimal shape).
resource "placecontext_job" "nightly_report" {
  project_id  = placecontext_project.storefront.id
  name        = "nightly-report"
  description = "Summarizes overnight orders"

  map_image      = "ghcr.io/acme/report-worker:latest"
  input_payloads = ["{}"]
  map_env = {
    REGION = "us-east-1"
  }

  allow_network_egress = false
  post_job_actions     = ["HtmlReport"]
  return_type          = "Json"
}

# A code-based, multi-file map job with parameters.
resource "placecontext_job" "etl_sync" {
  project_id     = placecontext_project.storefront.id
  name           = "etl-sync"
  map_runtime_id = "python"
  map_entrypoint = "main.py"

  # map_files / parameters are nested *attributes* — use list-of-objects
  # assignment syntax (with the `=`), not block syntax.
  map_files = [
    {
      path    = "main.py"
      content = "print('hello')\n"
    },
    {
      path    = "helpers.py"
      content = "def util():\n    ...\n"
    },
  ]

  input_payloads     = ["{\"batch\":1}", "{\"batch\":2}"]
  concurrency_limit  = 4
  success_exit_codes = [0]
  partial_exit_codes = [2]
  return_type        = "Table"

  parameters = [
    {
      name     = "region"
      label    = "Region"
      required = true
      type     = "string"
      options  = ["us-east-1", "eu-west-1"]
    },
  ]
}

# A cron schedule on the nightly report job.
resource "placecontext_schedule" "nightly" {
  project_id      = placecontext_project.storefront.id
  job_id          = placecontext_job.nightly_report.id
  name            = "nightly"
  kind            = "Schedule"
  cron_expression = "0 2 * * *"
  enabled         = true
}

# An event-triggered schedule.
resource "placecontext_schedule" "on_deploy" {
  project_id = placecontext_project.storefront.id
  job_id     = placecontext_job.etl_sync.id
  name       = "on-deploy"
  kind       = "Event"
  event_name = "deploy.finished"
}
