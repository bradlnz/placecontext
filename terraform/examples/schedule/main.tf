terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

# Cron schedule: the job runs at 02:00 every day.
resource "placecontext_schedule" "nightly" {
  project_id      = "00000000-0000-0000-0000-000000000000" # a placecontext_project.id
  job_id          = "11111111-1111-1111-1111-111111111111" # a placecontext_job.id
  name            = "Nightly rollup"
  kind            = "Schedule"
  cron_expression = "0 2 * * *"
  enabled         = true
}

# Event subscription: the job runs whenever the named event is published.
resource "placecontext_schedule" "on_order_import" {
  project_id = "00000000-0000-0000-0000-000000000000"
  job_id     = "11111111-1111-1111-1111-111111111111"
  name       = "On order import"
  kind       = "Event"
  event_name = "orders.imported"
  enabled    = true
}
