# ── Inputs for the PlaceContext public edge droplet on DigitalOcean ────────────────────────────────
# This droplet is NOT a standalone app host. It is a thin, publicly-reachable EDGE node that:
#   • joins the same self-hosted Headscale/Tailscale mesh the local cluster is on, and
#   • terminates public TLS (Caddy) and reverse-proxies to the PlaceContext app running on the
#     LOCAL k3s cluster over the tailnet.
# All workloads (the app, Postgres, MinIO, and every user job) run on the LOCAL cluster node — the
# droplet executes nothing and stores no data. See README.md for the topology diagram.

variable "do_token" {
  description = "DigitalOcean API token. Leave null to fall back to the DIGITALOCEAN_TOKEN env var."
  type        = string
  default     = null
  sensitive   = true
}

variable "name" {
  description = "Name/tag prefix for the created resources (droplet, firewall, ssh keys)."
  type        = string
  default     = "placecontext-edge"
}

variable "region" {
  description = "DigitalOcean region slug (e.g. nyc1, nyc3, sfo3, fra1, lon1, sgp1)."
  type        = string
  default     = "nyc3"
}

# ── Droplet sizing ────────────────────────────────────────────────────────────────────────────────
# Default s-2vcpu-2gb is the ~$18-20/mo tier. As a PURE EDGE (only Tailscale + Caddy run here — the
# droplet no longer needs headroom for Docker-in-Docker job containers, Postgres or MinIO), 2 GB is
# very comfortable. You could drop to s-1vcpu-1gb (~$6/mo) or s-1vcpu-2gb (~$12/mo) with no impact;
# s-2vcpu-2gb is kept as the default only to match the project's ~$20/mo target and leave slack.
variable "droplet_size" {
  description = "Droplet size slug. Default s-2vcpu-2gb ≈ $18-20/mo (roomy for a pure Tailscale+Caddy edge). s-1vcpu-1gb ≈ $6/mo is plenty for this role."
  type        = string
  default     = "s-2vcpu-2gb"
}

variable "image" {
  description = "Droplet base OS image slug."
  type        = string
  default     = "ubuntu-24-04-x64"
}

variable "ssh_public_keys" {
  description = "SSH public key material (the contents of e.g. ~/.ssh/id_ed25519.pub) granted root access to the droplet. At least one is required so you can operate/debug the edge over SSH."
  type        = list(string)

  validation {
    condition     = length(var.ssh_public_keys) > 0
    error_message = "Provide at least one SSH public key so you can reach the droplet."
  }
}

variable "ssh_allowed_cidrs" {
  description = "Source CIDRs allowed to reach SSH (port 22). Defaults to the whole internet; tighten to your admin IPs for production."
  type        = list(string)
  default     = ["0.0.0.0/0", "::/0"]
}

# ── Public hostname / TLS ─────────────────────────────────────────────────────────────────────────
variable "domain" {
  description = "Optional FQDN the portal answers on, e.g. app.example.com. When set, Caddy issues a Let's Encrypt cert for it (the name must resolve to this droplet's reserved IP — managed here when manage_dns = true). Leave empty to serve plain HTTP on port 80."
  type        = string
  default     = ""
}

variable "manage_dns" {
  description = "When true (and var.domain is set), create/manage the DNS A record for var.domain in DigitalOcean DNS pointing at the reserved IP. Set false if the domain's DNS lives elsewhere — you then point an A record at the reserved_ip output yourself."
  type        = bool
  default     = false
}

# ── Mesh (Headscale) join — mirrors the mesh the local cluster already uses ────────────────────────
# The existing deploy/terraform/ module provisions the Headscale control server, and each node joins
# it with a pre-auth key minted by `pctl mesh authkey --tenant <id>`. This edge joins the same way.
variable "mesh_control_url" {
  description = "Headscale control server URL the local cluster is meshed on, e.g. https://mesh.example.com (the `domain` output of deploy/terraform/). The droplet runs `tailscale up --login-server <this>`."
  type        = string
}

variable "mesh_authkey" {
  description = "Headscale pre-auth key for this node (mint on the mesh server: `pctl mesh authkey --tenant <id>`). Joins the droplet to the tailnet non-interactively."
  type        = string
  sensitive   = true
}

variable "mesh_hostname" {
  description = "Tailnet hostname to register the droplet under (visible in `tailscale status` / Headscale)."
  type        = string
  default     = "placecontext-edge"
}

# ── Upstream the edge proxies to (the app on the LOCAL cluster, over the tailnet) ──────────────────
variable "app_upstream" {
  description = "host:port of the PlaceContext portal/MCP as reachable OVER THE TAILNET — i.e. the local k3s node's tailnet IP (or Headscale hostname) and its ingress port. Example: `100.64.0.5:80` or `placecontext-local:7700`. Caddy reverse-proxies public traffic here."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z0-9._-]+:[0-9]+$", var.app_upstream))
    error_message = "app_upstream must be host:port, e.g. 100.64.0.5:80 or placecontext-local:7700 (no scheme)."
  }
}

variable "enable_backups" {
  description = "Enable DigitalOcean's automated weekly droplet backups (a stateless edge rarely needs this)."
  type        = bool
  default     = false
}

variable "monitoring" {
  description = "Install the DigitalOcean monitoring agent on the droplet."
  type        = bool
  default     = true
}
