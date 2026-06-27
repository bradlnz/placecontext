# ── PlaceContext mesh control plane on DigitalOcean ───────────────────────────────────────────────
# Provisions the droplet that runs the self-hosted Headscale (WireGuard) control server, gives it a
# stable reserved IP, locks the firewall down to exactly the ports Headscale needs, and (optionally)
# manages the DNS A record so Let's Encrypt can issue the control-server cert. Cloud-init brings the
# mesh up via `pctl mesh up`; manage tenants afterward with `pctl mesh tenant add|rm` over SSH.

locals {
  tags = [var.name]

  # apex domain + record name split out of the FQDN, e.g. mesh.example.com → example.com / mesh.
  domain_parts = split(".", var.domain)
  base_domain  = join(".", slice(local.domain_parts, length(local.domain_parts) - 2, length(local.domain_parts)))
  record_name  = length(local.domain_parts) > 2 ? join(".", slice(local.domain_parts, 0, length(local.domain_parts) - 2)) : "@"
}

# Register each provided public key with DigitalOcean and attach them to the droplet.
resource "digitalocean_ssh_key" "admin" {
  count      = length(var.ssh_public_keys)
  name       = "${var.name}-${count.index}"
  public_key = var.ssh_public_keys[count.index]
}

resource "digitalocean_droplet" "mesh" {
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

locals {
  cloud_init = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    domain          = var.domain
    repo_url        = var.repo_url
    repo_ref        = var.repo_ref
    headscale_image = var.headscale_image
  })
}

# Stable public IP that survives droplet rebuilds, so the DNS A record never has to change.
resource "digitalocean_reserved_ip" "mesh" {
  region = var.region
}

resource "digitalocean_reserved_ip_assignment" "mesh" {
  ip_address = digitalocean_reserved_ip.mesh.ip_address
  droplet_id = digitalocean_droplet.mesh.id
}

# Cloud firewall — only what Headscale needs, default-deny on everything else inbound.
resource "digitalocean_firewall" "mesh" {
  name        = var.name
  droplet_ids = [digitalocean_droplet.mesh.id]

  # SSH (administer the mesh with pctl over SSH).
  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = var.ssh_allowed_cidrs
  }

  # ACME HTTP-01 challenge for the Let's Encrypt control-server cert.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "80"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # Headscale control server (TLS) — nodes register and coordinate here.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "443"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # Embedded DERP/STUN relay for NAT traversal.
  inbound_rule {
    protocol         = "udp"
    port_range       = "3478"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # Allow all egress (apt, Docker pulls, Let's Encrypt, repo clone).
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

# DNS A record → reserved IP, so var.domain resolves to the control server and ACME can validate.
# Skip with manage_dns = false when the domain is hosted outside DigitalOcean.
resource "digitalocean_domain" "mesh" {
  count = var.manage_dns ? 1 : 0
  name  = local.base_domain
}

resource "digitalocean_record" "mesh" {
  count  = var.manage_dns ? 1 : 0
  domain = digitalocean_domain.mesh[0].name
  type   = "A"
  name   = local.record_name
  value  = digitalocean_reserved_ip.mesh.ip_address
  ttl    = 300
}
