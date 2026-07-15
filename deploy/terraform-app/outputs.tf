output "primary_reserved_ip" {
  description = "Stable public IP of the PRIMARY droplet (SSH / optional edge only — the DB is NOT reachable here)."
  value       = digitalocean_reserved_ip.primary.ip_address
}

output "primary_droplet_ipv4" {
  description = "The primary droplet's own (ephemeral) public IPv4."
  value       = digitalocean_droplet.primary.ipv4_address
}

output "primary_mesh_hostname" {
  description = "MagicDNS hostname of the primary on the tailnet — the read-write leader on first bootstrap."
  value       = var.mesh_hostname
}

output "witness_droplet_ipv4" {
  description = "The witness droplet's public IPv4 (SSH). Null when enable_witness = false."
  value       = var.enable_witness ? digitalocean_droplet.witness[0].ipv4_address : null
}

output "superuser_password" {
  description = "Postgres superuser password (supplied or generated). Feed it into the app connection string."
  value       = local.superuser_password
  sensitive   = true
}

output "replication_password" {
  description = "Physical-replication role password (supplied or generated). The local standby needs this to stream."
  value       = local.replication_password
  sensitive   = true
}

output "connection_string_hint" {
  description = "Npgsql multi-host connection string the app should use. Target Session Attributes=primary makes Npgsql probe each host and connect to whichever is the current read-write leader, so failover needs no app change."
  value       = "Host=${var.mesh_hostname},${var.replica_mesh_hostname};Port=5432;Database=${var.patroni_scope};Username=${var.superuser_username};Password=<superuser_password>;Target Session Attributes=primary;Load Balance Hosts=false"
  sensitive   = false
}

output "ssh_primary" {
  description = "SSH into the primary to run patronictl / etcdctl / inspect logs."
  value       = "ssh root@${digitalocean_reserved_ip.primary.ip_address}"
}

output "next_steps" {
  description = "How to verify the HA cluster once the droplets are up."
  value       = <<-EOT
    1. Wait for the primary to bootstrap (Docker + Tailscale + etcd + Patroni):
         ssh root@${digitalocean_reserved_ip.primary.ip_address} 'cloud-init status --wait && tailscale status && docker compose -f /opt/placecontext-db/docker-compose.yml ps'
    2. Confirm Patroni elected this node leader:
         ssh root@${digitalocean_reserved_ip.primary.ip_address} 'docker compose -f /opt/placecontext-db/docker-compose.yml exec -T patroni patronictl -c /etc/patroni/patroni.yml list'
    3. Bring up the LOCAL replica + its etcd member on the k3s node (over the mesh):
         (on the local node)  sudo ./deploy/pctl db-ha-join    # see deploy/ha/README.md
       ${var.enable_witness ? "   The witness etcd member is already running on ${var.witness_mesh_hostname}." : "   WARNING: enable_witness = false — you MUST supply a 3rd etcd member elsewhere or the cluster can split-brain."}
    4. Point the app at the cluster (multi-host, primary-targeting). On the local k3s node:
         kubectl -n placecontext set env deploy/placecontext \
           'PlaceContext__ConnectionString=Host=${var.mesh_hostname},${var.replica_mesh_hostname};Port=5432;Database=${var.patroni_scope};Username=${var.superuser_username};Password=REDACTED;Target Session Attributes=primary;Load Balance Hosts=false'
       (get the password from:  terraform output -raw superuser_password)
    5. Verify replication + leadership any time with patronictl list (step 2) — the standby shows State: streaming.
  EOT
}
