terraform {
  required_providers {
    placecontext = {
      source = "bradlnz/placecontext"
    }
  }
}

provider "placecontext" {
  # Prefer PLACECONTEXT_URL / PLACECONTEXT_API_KEY so the key never lands in
  # version control.
  # base_url = "https://acme.placecontext.io"
  # api_key  = var.placecontext_api_key
}
