# ── PlaceContext public edge on a single DigitalOcean droplet ─────────────────────────────────────
# Topology:
#
#   Internet ──443/80──▶ [ DO droplet: Caddy (TLS) ]
#                              │  joins the Headscale mesh (tailscale up --login-server)
#                              ▼  reverse_proxy over the tailnet
#                        Tailscale/Headscale mesh  ◀── deploy/terraform/ provisions the control server
#                              │
#                              ▼
#                    [ LOCAL k3s cluster (behind NAT) ]
#                      • PlaceContext app (portal + MCP + scheduler)
#                      • Postgres (pgvector) + MinIO
#                      • ALL user job pods (KubernetesWorkloadRunner) run HERE
#
# The droplet is a stateless, publicly-reachable EDGE: it terminates TLS and forwards to the app on
# the local cluster over the mesh. It runs no app/DB/jobs and mounts no Docker socket.

locals {
  tags = [var.name]

  # apex domain + record name split out of the FQDN, e.g. app.example.com → example.com / app.
  # Guarded so an empty var.domain (plain-HTTP mode) never hits the slice() below.
  domain_parts = var.domain != "" ? split(".", var.domain) : []
  base_domain  = var.domain != "" ? join(".", slice(local.domain_parts, length(local.domain_parts) - 2, length(local.domain_parts))) : ""
  record_name  = var.domain != "" ? (length(local.domain_parts) > 2 ? join(".", slice(local.domain_parts, 0, length(local.domain_parts) - 2)) : "@") : "@"

  # DNS is only managed when explicitly requested AND a domain was actually provided.
  manage_dns = var.manage_dns && var.domain != ""

  cloud_init = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    domain           = var.domain
    mesh_control_url = var.mesh_control_url
    mesh_authkey     = var.mesh_authkey
    mesh_hostname    = var.mesh_hostname
    app_upstream     = var.app_upstream
  })
}

# Register each provided public key with DigitalOcean and attach them to the droplet.
resource "digitalocean_ssh_key" "admin" {
  count      = length(var.ssh_public_keys)
  name       = "${var.name}-${count.index}"
  public_key = var.ssh_public_keys[count.index]
}

resource "digitalocean_droplet" "edge" {
  name       = var.name
  region     = var.region
  size       = var.droplet_size
  image      = var.image
  ssh_keys   = digitalocean_ssh_key.admin[*].fingerprint
  user_data  = local.cloud_init
  backups    = var.enable_backups
  monitoring = var.monitoring
  ipv6       = true
  tags       = local.tags
}

# Stable public IP that survives droplet rebuilds, so a DNS A record never has to change.
resource "digitalocean_reserved_ip" "edge" {
  region = var.region
}

resource "digitalocean_reserved_ip_assignment" "edge" {
  ip_address = digitalocean_reserved_ip.edge.ip_address
  droplet_id = digitalocean_droplet.edge.id
}

# Cloud firewall — public web + SSH only. The mesh/tailnet is reached via outbound (Headscale control
# server on 443 + DERP relay), so no inbound mesh ports are needed here.
resource "digitalocean_firewall" "edge" {
  name        = var.name
  droplet_ids = [digitalocean_droplet.edge.id]

  # SSH (operate/debug the edge). Tighten with var.ssh_allowed_cidrs for production.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = var.ssh_allowed_cidrs
  }

  # HTTP — plain-HTTP portal (no domain) and the Let's Encrypt HTTP-01 challenge / 80→443 redirect.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "80"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # HTTPS — the portal + MCP behind Caddy's auto-issued TLS cert (when var.domain is set).
  inbound_rule {
    protocol         = "tcp"
    port_range       = "443"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # Allow all egress (apt, Docker/Caddy pulls, Tailscale/Headscale mesh + DERP, Let's Encrypt, …).
  outbound_rule {
    protocol              = "tcp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
  outbound_rule {
    protocol              = "udp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
  outbound_rule {
    protocol              = "icmp"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
}

# DNS A record → reserved IP so var.domain resolves to the edge and Caddy's ACME can validate.
# Only created when manage_dns = true AND a domain is set; the apex must be hosted in DigitalOcean DNS.
resource "digitalocean_domain" "edge" {
  count = local.manage_dns ? 1 : 0
  name  = local.base_domain
}

resource "digitalocean_record" "edge" {
  count  = local.manage_dns ? 1 : 0
  domain = digitalocean_domain.edge[0].name
  type   = "A"
  name   = local.record_name
  value  = digitalocean_reserved_ip.edge.ip_address
  ttl    = 300
}
