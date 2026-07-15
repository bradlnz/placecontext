# ── PlaceContext HA-Postgres PRIMARY on a DigitalOcean droplet ────────────────────────────────────
#
# INVERSION NOTE: an earlier iteration of this module made the droplet a thin, stateless *mesh edge*
# with the database on the local cluster. It is now the opposite — the DO droplet hosts the read-write
# Postgres PRIMARY. Topology:
#
#                                  ┌──────────────────────────────────────┐
#   Internet ─22/80/443(opt edge)─▶│  DO droplet (this module) — PRIMARY   │
#                                  │  • Patroni + Postgres 16 (pgvector)   │  read-write leader
#                                  │  • etcd member #1  (DCS)              │
#                                  │  • Tailscale → joins the mesh         │
#                                  └───────────────┬──────────────────────┘
#                     async streaming replication  │  (tailnet ONLY — never public)
#                        + etcd peer traffic        ▼
#                                  Headscale / Tailscale mesh
#                     ┌───────────────────────────┼───────────────────────────┐
#                     ▼                                                        ▼
#   ┌──────────────────────────────────┐              ┌──────────────────────────────────┐
#   │ LOCAL k3s node (deploy/ha/)       │              │ WITNESS droplet (this module)     │
#   │ • Patroni + Postgres hot-standby  │              │ • etcd member #3 (tiebreaker)     │
#   │ • etcd member #2  (DCS)           │              │ • NO Postgres                     │
#   └──────────────────────────────────┘              └──────────────────────────────────┘
#
# The three etcd members give a 2-of-3 majority: if the DO site is unreachable, the LOCAL replica +
# WITNESS still form a quorum and Patroni auto-promotes the local standby. A single-site partition can
# NEVER hold a majority alone, so it cannot promote a second primary (split-brain guard).
#
# Replication is ASYNCHRONOUS (synchronous_mode: false) so the primary never blocks on tailnet latency
# and the local standby stays fresh without back-pressure. Trade-off: a small data-loss window on an
# UNCLEAN primary failure (transactions committed on DO but not yet streamed). Documented in README.md.

locals {
  tags         = [var.name]
  witness_tags = ["${var.name}-witness"]

  # apex domain + record name split out of the FQDN (optional co-located edge).
  domain_parts = var.domain != "" ? split(".", var.domain) : []
  base_domain  = var.domain != "" ? join(".", slice(local.domain_parts, length(local.domain_parts) - 2, length(local.domain_parts))) : ""
  record_name  = var.domain != "" ? (length(local.domain_parts) > 2 ? join(".", slice(local.domain_parts, 0, length(local.domain_parts) - 2)) : "@") : "@"
  manage_dns   = var.manage_dns && var.domain != ""

  # Round-robin DB DNS: split the requested name into apex + record, and create one A/AAAA per value.
  rr_enabled     = var.db_rr_dns_name != "" && length(var.db_rr_dns_values) > 0
  rr_parts       = var.db_rr_dns_name != "" ? split(".", var.db_rr_dns_name) : []
  rr_base_domain = var.db_rr_dns_name != "" ? join(".", slice(local.rr_parts, length(local.rr_parts) - 2, length(local.rr_parts))) : ""
  rr_record_name = var.db_rr_dns_name != "" ? (length(local.rr_parts) > 2 ? join(".", slice(local.rr_parts, 0, length(local.rr_parts) - 2)) : "@") : "@"
  rr_ipv4_values = [for v in var.db_rr_dns_values : v if !strcontains(v, ":")]
  rr_ipv6_values = [for v in var.db_rr_dns_values : v if strcontains(v, ":")]

  # etcd initial cluster: all three members addressed by their MagicDNS names over the tailnet.
  etcd_initial_cluster = join(",", [
    "${var.mesh_hostname}=http://${var.mesh_hostname}:2380",
    "${var.replica_mesh_hostname}=http://${var.replica_mesh_hostname}:2380",
    "${var.witness_mesh_hostname}=http://${var.witness_mesh_hostname}:2380",
  ])

  superuser_password   = var.superuser_password != null ? var.superuser_password : random_password.superuser.result
  replication_password = var.replication_password != null ? var.replication_password : random_password.replication.result

  cloud_init_primary = templatefile("${path.module}/cloud-init-primary.yaml.tftpl", {
    domain               = var.domain
    mesh_control_url     = var.mesh_control_url
    mesh_authkey         = var.mesh_authkey
    mesh_hostname        = var.mesh_hostname
    patroni_scope        = var.patroni_scope
    patroni_image        = var.patroni_image
    etcd_image           = var.etcd_image
    etcd_name            = var.mesh_hostname
    etcd_initial_cluster = local.etcd_initial_cluster
    superuser_username   = var.superuser_username
    superuser_password   = local.superuser_password
    replication_username = var.replication_username
    replication_password = local.replication_password
  })

  cloud_init_witness = templatefile("${path.module}/cloud-init-witness.yaml.tftpl", {
    mesh_control_url     = var.mesh_control_url
    mesh_authkey         = var.witness_mesh_authkey
    mesh_hostname        = var.witness_mesh_hostname
    etcd_image           = var.etcd_image
    etcd_name            = var.witness_mesh_hostname
    etcd_initial_cluster = local.etcd_initial_cluster
  })
}

# Auto-generated credentials when the operator does not supply their own (both marked sensitive).
resource "random_password" "superuser" {
  length  = 28
  special = false # keep it URL/connection-string safe (no @ : / in the password)
}

resource "random_password" "replication" {
  length  = 28
  special = false
}

# Register each provided public key with DigitalOcean and attach to the droplets.
resource "digitalocean_ssh_key" "admin" {
  count      = length(var.ssh_public_keys)
  name       = "${var.name}-${count.index}"
  public_key = var.ssh_public_keys[count.index]
}

# ── Primary droplet ───────────────────────────────────────────────────────────────────────────────
resource "digitalocean_droplet" "primary" {
  name       = "${var.name}-primary"
  region     = var.region
  size       = var.droplet_size
  image      = var.image
  ssh_keys   = digitalocean_ssh_key.admin[*].fingerprint
  user_data  = local.cloud_init_primary
  backups    = var.enable_backups
  monitoring = var.monitoring
  ipv6       = true
  tags       = local.tags
}

# Stable public IP so an optional public A record never has to change on rebuild.
resource "digitalocean_reserved_ip" "primary" {
  region = var.region
}

resource "digitalocean_reserved_ip_assignment" "primary" {
  ip_address = digitalocean_reserved_ip.primary.ip_address
  droplet_id = digitalocean_droplet.primary.id
}

# Cloud firewall for the primary.
#
# IMPORTANT — how "tailnet-only" is enforced: Tailscale/WireGuard traffic to the 100.x tailnet does
# NOT traverse this cloud firewall as tcp/5432 etc.; it arrives encrypted (UDP 41641 / DERP over 443)
# and is decrypted on the tailscale0 interface. So we simply DO NOT open 5432 / 8008 (Patroni REST) /
# 2379-2380 (etcd) publicly here (default-deny), and the bootstrap binds those services to the node's
# tailnet IP — belt and suspenders. Result: Postgres, replication and the DCS are reachable ONLY over
# the mesh and never on the public IP. Only SSH + the optional edge (80/443) are public.
resource "digitalocean_firewall" "primary" {
  name        = "${var.name}-primary"
  droplet_ids = [digitalocean_droplet.primary.id]

  # SSH — operate the DB (patronictl, etcdctl, logs). Tighten with var.ssh_allowed_cidrs.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = var.ssh_allowed_cidrs
  }

  # HTTP — optional co-located Caddy edge / Let's Encrypt HTTP-01. Not used by the database.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "80"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # HTTPS — optional co-located edge. Not used by the database.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "443"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  # NOTE: 5432 (Postgres), 8008 (Patroni REST), 2379/2380 (etcd) are deliberately NOT listed —
  # they must never be public. They are reached only over the tailnet (tailscale0), and the services
  # bind to the tailnet IP. Do not add public inbound rules for them.

  # Allow all egress (apt, image pulls, Tailscale/Headscale + DERP, Let's Encrypt).
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

# ── Witness droplet (third etcd member — the split-brain guard) ───────────────────────────────────
resource "digitalocean_droplet" "witness" {
  count      = var.enable_witness ? 1 : 0
  name       = "${var.name}-witness"
  region     = var.witness_region
  size       = var.witness_size
  image      = var.image
  ssh_keys   = digitalocean_ssh_key.admin[*].fingerprint
  user_data  = local.cloud_init_witness
  monitoring = var.monitoring
  ipv6       = true
  tags       = local.witness_tags
}

resource "digitalocean_firewall" "witness" {
  count       = var.enable_witness ? 1 : 0
  name        = "${var.name}-witness"
  droplet_ids = [digitalocean_droplet.witness[0].id]

  # SSH only in public. etcd peer/client (2379/2380) ride the tailnet exactly as on the primary.
  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = var.ssh_allowed_cidrs
  }

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

# ── Optional public edge DNS (A record for var.domain → reserved IP) ──────────────────────────────
resource "digitalocean_domain" "edge" {
  count = local.manage_dns ? 1 : 0
  name  = local.base_domain
}

resource "digitalocean_record" "edge" {
  count  = local.manage_dns ? 1 : 0
  domain = digitalocean_domain.edge[0].name
  type   = "A"
  name   = local.record_name
  value  = digitalocean_reserved_ip.primary.ip_address
  ttl    = 300
}

# ── Optional round-robin DB DNS (one A/AAAA record per node tailnet IP) ───────────────────────────
resource "digitalocean_domain" "db_rr" {
  count = local.rr_enabled ? 1 : 0
  name  = local.rr_base_domain
}

resource "digitalocean_record" "db_rr_a" {
  count  = local.rr_enabled ? length(local.rr_ipv4_values) : 0
  domain = digitalocean_domain.db_rr[0].name
  type   = "A"
  name   = local.rr_record_name
  value  = local.rr_ipv4_values[count.index]
  ttl    = 60
}

resource "digitalocean_record" "db_rr_aaaa" {
  count  = local.rr_enabled ? length(local.rr_ipv6_values) : 0
  domain = digitalocean_domain.db_rr[0].name
  type   = "AAAA"
  name   = local.rr_record_name
  value  = local.rr_ipv6_values[count.index]
  ttl    = 60
}
