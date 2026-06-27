# ── Inputs for the Headscale mesh control-plane droplet on DigitalOcean ───────────────────────────

variable "do_token" {
  description = "DigitalOcean API token. Leave null to fall back to the DIGITALOCEAN_TOKEN env var."
  type        = string
  default     = null
  sensitive   = true
}

variable "domain" {
  description = "FQDN the mesh control server answers on, e.g. mesh.example.com. Headscale's Let's Encrypt cert is issued for this name, so it must resolve to the droplet (managed here when manage_dns = true)."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9.-]+\\.[a-z]{2,}$", var.domain))
    error_message = "domain must be a fully-qualified hostname like mesh.example.com."
  }
}

variable "name" {
  description = "Name/tag prefix for the created resources."
  type        = string
  default     = "placecontext-mesh"
}

variable "region" {
  description = "DigitalOcean region slug (e.g. nyc1, nyc3, sfo3, fra1, lon1, sgp1)."
  type        = string
  default     = "nyc3"
}

variable "droplet_size" {
  description = "Droplet size slug. Headscale is light; the smallest shared-CPU box is plenty."
  type        = string
  default     = "s-1vcpu-1gb"
}

variable "image" {
  description = "Droplet base image slug."
  type        = string
  default     = "ubuntu-24-04-x64"
}

variable "ssh_public_keys" {
  description = "SSH public key material (the contents of e.g. ~/.ssh/id_ed25519.pub) granted root access to the droplet. At least one is required so you can operate the mesh with pctl over SSH."
  type        = list(string)

  validation {
    condition     = length(var.ssh_public_keys) > 0
    error_message = "Provide at least one SSH public key so you can reach the droplet."
  }
}

variable "manage_dns" {
  description = "When true, create/manage the DNS A record for var.domain in DigitalOcean DNS. Set false if the domain's DNS lives elsewhere — you then point an A record at the reserved_ip output yourself."
  type        = bool
  default     = true
}

variable "headscale_image" {
  description = "Headscale container image to run on the droplet."
  type        = string
  default     = "headscale/headscale:0.23.0"
}

variable "repo_url" {
  description = "Git URL the droplet clones to get pctl + the deploy/headscale stack. Use an https://<token>@github.com/... form if the repo is private."
  type        = string
  default     = "https://github.com/bradlnz/placecontext.git"
}

variable "repo_ref" {
  description = "Branch or tag of repo_url to check out on the droplet."
  type        = string
  default     = "main"
}

variable "ssh_allowed_cidrs" {
  description = "Source CIDRs allowed to reach SSH (port 22). Defaults to the whole internet; tighten to your admin IPs for production."
  type        = list(string)
  default     = ["0.0.0.0/0", "::/0"]
}

variable "enable_backups" {
  description = "Enable DigitalOcean's automated weekly droplet backups."
  type        = bool
  default     = false
}

variable "monitoring" {
  description = "Install the DigitalOcean monitoring agent on the droplet."
  type        = bool
  default     = true
}
