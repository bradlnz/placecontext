# Provider + Terraform version constraints for the PlaceContext HA-Postgres primary deployment.
terraform {
  required_version = ">= 1.5.0"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.43"
    }
    # Used to generate the replication + superuser passwords when they are not supplied.
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

# Token comes from var.do_token (or the DIGITALOCEAN_TOKEN env var if left null).
provider "digitalocean" {
  token = var.do_token
}
