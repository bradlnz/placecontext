# ── Inputs for the PlaceContext HA-Postgres PRIMARY droplet on DigitalOcean ───────────────────────
#
# This droplet hosts the read-write Postgres PRIMARY (Patroni-managed, pgvector) plus one member of
# the 3-node etcd DCS. It joins the same self-hosted Headscale/Tailscale mesh the local k3s cluster
# is on. Local node(s) run hot-standby ASYNC replicas + a second etcd member; a small WITNESS droplet
# runs the third etcd member so the cluster keeps an odd 2-of-3 quorum and a single-site partition
# cannot promote two primaries. Streaming replication + the DCS ride the tailnet only — never public.
# See README.md / deploy/ha/README.md for the topology diagram and the failover/failback runbook.

variable "do_token" {
  description = "DigitalOcean API token. Leave null to fall back to the DIGITALOCEAN_TOKEN env var."
  type        = string
  default     = null
  sensitive   = true
}

variable "name" {
  description = "Name/tag prefix for the created resources (droplets, firewalls, ssh keys)."
  type        = string
  default     = "placecontext-db"
}

variable "region" {
  description = "DigitalOcean region slug for the PRIMARY (e.g. nyc1, nyc3, sfo3, fra1, lon1, sgp1)."
  type        = string
  default     = "nyc3"
}

# ── Droplet sizing ────────────────────────────────────────────────────────────────────────────────
# This box now runs Postgres (the primary) + etcd + Patroni, so it needs real RAM/disk headroom —
# unlike the old thin edge. s-2vcpu-4gb (~$24/mo) is a sane floor for a small production DB.
variable "droplet_size" {
  description = "Primary droplet size slug. Default s-2vcpu-4gb ≈ $24/mo (room for Postgres + etcd + Patroni)."
  type        = string
  default     = "s-2vcpu-4gb"
}

variable "image" {
  description = "Droplet base OS image slug."
  type        = string
  default     = "ubuntu-24-04-x64"
}

variable "ssh_public_keys" {
  description = "SSH public key material (contents of e.g. ~/.ssh/id_ed25519.pub) granted root on the droplets. At least one is required so you can operate/debug over SSH (patronictl, etcdctl, logs)."
  type        = list(string)

  validation {
    condition     = length(var.ssh_public_keys) > 0
    error_message = "Provide at least one SSH public key so you can reach the droplets."
  }
}

variable "ssh_allowed_cidrs" {
  description = "Source CIDRs allowed to reach SSH (port 22). Defaults to the whole internet; tighten to your admin IPs for production."
  type        = list(string)
  default     = ["0.0.0.0/0", "::/0"]
}

# ── Public hostname / TLS (optional co-located edge) ──────────────────────────────────────────────
# The primary's core job is the database, but 80/443 are kept open so this box can OPTIONALLY also run
# a public Caddy edge / ACME challenge (or you front it with deploy/terraform/). The DB itself is
# never reachable on these ports.
variable "domain" {
  description = "Optional FQDN for a public edge co-located on this droplet (leave empty for a pure DB host). Not required for the database."
  type        = string
  default     = ""
}

variable "manage_dns" {
  description = "When true (and var.domain is set), manage the public A record for var.domain in DigitalOcean DNS pointing at the reserved IP."
  type        = bool
  default     = false
}

# ── Round-robin DB DNS (optional, ops convenience) ────────────────────────────────────────────────
# The RELIABLE way the app finds the current leader is the Npgsql multi-host connection string
# (Host=primary,replica;Target Session Attributes=primary) over Headscale MagicDNS — see README.
# Optionally you can ALSO publish a single round-robin DNS name whose A records list the node tailnet
# IPs, for humans / tools that just want one endpoint. Left empty → no such records are created.
variable "db_rr_dns_name" {
  description = "Optional round-robin DNS name for the DB endpoint, e.g. db.example.com (its apex must be hosted in DigitalOcean DNS). Empty → not created."
  type        = string
  default     = ""
}

variable "db_rr_dns_values" {
  description = "Tailnet IPs (100.x / fd7a:…) of the DB nodes to publish as round-robin A/AAAA records under db_rr_dns_name. Order-insensitive; one record per value. Empty → none."
  type        = list(string)
  default     = []
}

# ── Mesh (Headscale) join — mirrors the mesh the local cluster already uses ────────────────────────
variable "mesh_control_url" {
  description = "Headscale control server URL the local cluster is meshed on, e.g. https://mesh.example.com (the `domain` output of deploy/terraform/)."
  type        = string
}

variable "mesh_authkey" {
  description = "Headscale pre-auth key for the PRIMARY node (mint on the mesh server: `pctl mesh authkey --tenant <id>`)."
  type        = string
  sensitive   = true
}

variable "mesh_hostname" {
  description = "Tailnet (MagicDNS) hostname to register the PRIMARY under. Used verbatim in etcd peer URLs and the app connection string, so keep it stable."
  type        = string
  default     = "placecontext-db-primary"
}

# ── DCS topology (the three etcd members, addressed by MagicDNS name) ──────────────────────────────
# etcd forms its initial cluster from these three MagicDNS names; all three must resolve on the tailnet
# for the DCS to reach quorum. The primary + witness are provisioned here; the replica is the local
# k3s node brought up by deploy/ha/ (its Patroni + etcd member join the same scope).
variable "replica_mesh_hostname" {
  description = "MagicDNS hostname of the LOCAL replica node (deploy/ha/). Must match what the local node registers under. Used in the etcd initial-cluster + the app connection string."
  type        = string
  default     = "placecontext-db-local"
}

variable "patroni_scope" {
  description = "Patroni cluster scope / etcd key namespace and the DB name. The app database is created under this."
  type        = string
  default     = "placecontext"
}

# ── Witness (third etcd member — the split-brain guard) ───────────────────────────────────────────
variable "enable_witness" {
  description = "Provision the witness droplet (third etcd member). STRONGLY recommended: without a 3rd member a 2-site cluster has no majority and WILL split-brain on a partition. Only disable if you provide a 3rd etcd member elsewhere on the mesh."
  type        = bool
  default     = true
}

variable "witness_region" {
  description = "DigitalOcean region for the witness. Use a DIFFERENT region from var.region so a single-region outage can't take out both the primary and the tiebreaker."
  type        = string
  default     = "fra1"
}

variable "witness_size" {
  description = "Witness droplet size. It runs only an etcd member (no Postgres), so the smallest box is plenty."
  type        = string
  default     = "s-1vcpu-1gb"
}

variable "witness_mesh_authkey" {
  description = "Headscale pre-auth key for the WITNESS node. Ignored when enable_witness = false."
  type        = string
  default     = ""
  sensitive   = true
}

variable "witness_mesh_hostname" {
  description = "MagicDNS hostname for the witness (third etcd member). Used in the etcd initial-cluster on every node."
  type        = string
  default     = "placecontext-db-witness"
}

# ── Database / replication credentials (sensitive; generated when left null) ──────────────────────
variable "superuser_username" {
  description = "Postgres superuser the app connects as. Kept as `postgres` for continuity with the existing connection string."
  type        = string
  default     = "postgres"
}

variable "superuser_password" {
  description = "Password for the superuser. Leave null to auto-generate (see the sensitive `superuser_password` output / connection_string_hint)."
  type        = string
  default     = null
  sensitive   = true
}

variable "replication_username" {
  description = "Least-privilege physical-replication role the standbys stream as (LOGIN + REPLICATION only, no data access)."
  type        = string
  default     = "replicator"
}

variable "replication_password" {
  description = "Password for the replication role. Leave null to auto-generate."
  type        = string
  default     = null
  sensitive   = true
}

# ── Container images ──────────────────────────────────────────────────────────────────────────────
variable "patroni_image" {
  description = "Patroni + PostgreSQL 16 + pgvector image (built from deploy/postgres/Dockerfile.patroni and pushed to a registry the droplet can pull). MUST provide the `vector` extension."
  type        = string
  default     = "ghcr.io/bradlnz/placecontext-patroni:16-pgvector"
}

variable "etcd_image" {
  description = "etcd image for the DCS members."
  type        = string
  default     = "quay.io/coreos/etcd:v3.5.16"
}

# ── Firewall / mesh CIDRs ─────────────────────────────────────────────────────────────────────────
variable "tailnet_cidrs" {
  description = "Tailnet CIDRs (Headscale defaults: CGNAT 100.64.0.0/10 + ULA fd7a:115c:a1e0::/48). Used only for documentation/pg_hba scoping — DCS/DB/replication ports are NOT opened on the public cloud firewall; tailnet traffic arrives decrypted on tailscale0 and services bind to the tailnet IP."
  type        = list(string)
  default     = ["100.64.0.0/10", "fd7a:115c:a1e0::/48"]
}

variable "enable_backups" {
  description = "Enable DigitalOcean's automated weekly droplet backups for the primary."
  type        = bool
  default     = false
}

variable "monitoring" {
  description = "Install the DigitalOcean monitoring agent on the droplets."
  type        = bool
  default     = true
}
