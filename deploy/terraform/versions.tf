# Provider + Terraform version constraints for the PlaceContext mesh control plane.
terraform {
  required_version = ">= 1.5.0"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.43"
    }
  }
}

# Token comes from var.do_token (or the DIGITALOCEAN_TOKEN env var if left null).
provider "digitalocean" {
  token = var.do_token
}
