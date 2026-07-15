output "reserved_ip" {
  description = "Stable public IP of the edge droplet. Point var.domain's A record here if manage_dns = false."
  value       = digitalocean_reserved_ip.edge.ip_address
}

output "droplet_ipv4" {
  description = "The droplet's own (ephemeral) public IPv4."
  value       = digitalocean_droplet.edge.ipv4_address
}

output "portal_url" {
  description = "Public URL the portal + MCP answer on once the edge is up and the local app is reachable over the mesh."
  value       = var.domain != "" ? "https://${var.domain}" : "http://${digitalocean_reserved_ip.edge.ip_address}"
}

output "ssh" {
  description = "SSH into the edge droplet to inspect Tailscale + Caddy."
  value       = "ssh root@${digitalocean_reserved_ip.edge.ip_address}"
}

output "next_steps" {
  description = "How to verify the edge once the droplet is up."
  value       = <<-EOT
    1. Wait for cloud-init to install Docker + Tailscale, join the mesh, and start Caddy:
         ssh root@${digitalocean_reserved_ip.edge.ip_address} 'cloud-init status --wait && tailscale status && docker compose -f /opt/placecontext-edge/docker-compose.yml ps'
    2. Confirm the edge can reach the app on the local cluster over the tailnet:
         ssh root@${digitalocean_reserved_ip.edge.ip_address} 'curl -sS -o /dev/null -w "%%{http_code}\n" http://${var.app_upstream}/healthz'
    3. Reach the portal publicly:
         ${var.domain != "" ? "https://${var.domain}" : "http://${digitalocean_reserved_ip.edge.ip_address}"}
       ${var.domain != "" ? "(Caddy issues a Let's Encrypt cert once ${var.domain} resolves to ${digitalocean_reserved_ip.edge.ip_address}.)" : "(Set var.domain + a DNS A record to get automatic HTTPS.)"}
    4. Tail Caddy logs if needed:
         ssh root@${digitalocean_reserved_ip.edge.ip_address} 'docker compose -f /opt/placecontext-edge/docker-compose.yml logs -f caddy'
  EOT
}
