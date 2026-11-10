terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

# Projects are idempotent by path. The management API cannot rename or delete a
# project, so a destroyed resource stays in the workspace (and is only removed
# from Terraform state).
resource "placecontext_project" "revenue" {
  path = "data/revenue"
  name = "Revenue"
}

# Read a project back by path instead of managing it here.
data "placecontext_project" "existing" {
  path = "data/customers"
}

output "existing_project_id" {
  value = data.placecontext_project.existing.id
}
