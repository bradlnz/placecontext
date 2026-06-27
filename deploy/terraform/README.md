# Terraform — PlaceContext mesh control plane (DigitalOcean)

Provisions the DigitalOcean infrastructure for the self-hosted **Headscale** (WireGuard) mesh
control server that `deploy/headscale/` + `pctl mesh` drive. This is the infrastructure-as-code
counterpart to the manual "spin up a droplet and run `pctl mesh up`" flow in
[`docs/SETUP.md`](../../docs/SETUP.md).

What it creates:

| Resource | Purpose |
|----------|---------|
| `digitalocean_droplet` | Ubuntu box; cloud-init installs Docker, clones the repo, runs `pctl mesh up` |
| `digitalocean_reserved_ip` (+ assignment) | Stable public IP that survives droplet rebuilds |
| `digitalocean_firewall` | Default-deny inbound; opens only `22`, `80`, `443/tcp` and `3478/udp` |
| `digitalocean_domain` + `digitalocean_record` | A record `domain → reserved IP` (skip with `manage_dns = false`) |
| `digitalocean_ssh_key` | Your admin key(s) for SSH/`pctl` access |

The droplet ends up byte-for-byte what `pctl mesh up` produces, so **every `pctl mesh` command works
natively on it** once it's up.

## Ports

Cloud + host firewalls open exactly what Headscale needs (see `deploy/headscale/docker-compose.yml`):

- `80/tcp` — Let's Encrypt HTTP-01 challenge
- `443/tcp` — control server (TLS)
- `3478/udp` — embedded DERP/STUN for NAT traversal
- `22/tcp` — SSH (restrict via `ssh_allowed_cidrs` for production)

## Usage

```bash
cd deploy/terraform
export DIGITALOCEAN_TOKEN=dop_v1_...        # or set do_token in terraform.tfvars
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars                    # set domain + ssh_public_keys

terraform init
terraform plan
terraform apply
```

`terraform output next_steps` prints the post-apply runbook (wait for cloud-init, add a tenant,
mint a key, join a cluster).

### DNS

- **`manage_dns = true` (default):** the apex domain must be hosted in DigitalOcean DNS; the A
  record is created for you.
- **`manage_dns = false`:** create an A record yourself pointing `domain` at the `reserved_ip`
  output. Let's Encrypt can't issue the control-server cert until that record resolves.

### Private repo

`repo_url` defaults to the public GitHub URL. If the repo is private, pass a token-bearing URL:

```hcl
repo_url = "https://<github-token>@github.com/bradlnz/placecontext.git"
```

## Operating the mesh

SSH in and use `pctl` (single source of truth — same commands as a hand-rolled droplet):

```bash
ssh root@<reserved_ip>
cd /opt/placecontext
./deploy/pctl mesh tenant add acme        # isolated, ACL-enforced network for a customer
./deploy/pctl mesh authkey --tenant acme  # persistent pre-auth key for that tenant's nodes
```

## Teardown

```bash
terraform destroy
```

State (`*.tfstate`) and `terraform.tfvars` are gitignored — they can contain the API token and
infra details. Use a remote backend for team/shared use.
