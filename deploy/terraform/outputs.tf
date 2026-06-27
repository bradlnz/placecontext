output "reserved_ip" {
  description = "Stable public IP of the mesh control server. Point var.domain's A record here if manage_dns = false."
  value       = digitalocean_reserved_ip.mesh.ip_address
}

output "droplet_ipv4" {
  description = "The droplet's own (ephemeral) public IPv4."
  value       = digitalocean_droplet.mesh.ipv4_address
}

output "mesh_url" {
  description = "Headscale control-server URL — pass to nodes as --vpn-control."
  value       = "https://${var.domain}:443"
}

output "ssh" {
  description = "SSH into the control server to manage tenants/keys with pctl."
  value       = "ssh root@${digitalocean_reserved_ip.mesh.ip_address}"
}

output "next_steps" {
  description = "How to drive the mesh once the droplet is up."
  value       = <<-EOT
    1. Wait for cloud-init to finish (DNS must resolve ${var.domain} → ${digitalocean_reserved_ip.mesh.ip_address}
       before Let's Encrypt can issue the cert). Tail it with:
         ssh root@${digitalocean_reserved_ip.mesh.ip_address} 'cloud-init status --wait && tail -n40 /var/log/cloud-init-output.log'
    2. Add an isolated tenant network and mint its node key (run on the droplet):
         ssh root@${digitalocean_reserved_ip.mesh.ip_address}
         cd /opt/placecontext
         ./deploy/pctl mesh tenant add <customer>
         ./deploy/pctl mesh authkey --tenant <customer>
    3. Join a cluster to the mesh:
         sudo ./deploy/pctl server up --vpn-control https://${var.domain}:443 --vpn-authkey <KEY>
  EOT
}
