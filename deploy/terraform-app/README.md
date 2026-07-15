# Terraform — PlaceContext HA-Postgres PRIMARY (DigitalOcean)

Provisions the **read-write PostgreSQL PRIMARY** for PlaceContext on a DigitalOcean droplet, plus a
small **witness** droplet (the third DCS vote). The primary streams **asynchronously** to a
hot-standby on the **local k3s node** over the self-hosted **Headscale/Tailscale mesh**, and Patroni
**auto-promotes** the local standby if DO goes offline.

> **Inversion note.** An earlier version of this module made the droplet a thin, stateless *mesh edge*
> with the database on the local cluster. **That is reversed here:** the droplet now hosts the Postgres
> **primary** (Patroni + etcd), and the local node hosts the standby. The old edge behavior (public
> Caddy TLS reverse-proxy) is not this module's job anymore; front the app with a separate edge or the
> optional co-located 80/443 (left open but unused by the DB). The full design, failover/failback
> runbook, and the local-replica setup live in **[`deploy/ha/README.md`](../ha/README.md)** — read
> that first.

## What it creates

| Resource | Purpose |
|----------|---------|
| `digitalocean_droplet.primary` | Postgres **primary**: cloud-init installs Docker + Tailscale, joins the mesh, runs **etcd #1 + Patroni/Postgres 16 (pgvector)** bound to the tailnet IP |
| `digitalocean_droplet.witness` | **etcd #3** only (no Postgres) in a *different region* — the 2-of-3 tiebreaker (`enable_witness = true`) |
| `digitalocean_reserved_ip` (+ assignment) | Stable public IP for the primary (SSH / optional edge) |
| `digitalocean_firewall.primary` / `.witness` | Public: **22 / 80 / 443** only. **5432 / 8008 / 2379 / 2380 are NEVER opened** — DB/replication/DCS ride the tailnet |
| `digitalocean_record.db_rr_*` | Optional round-robin A/AAAA records for a single DB DNS name (`db_rr_dns_*`) |
| `random_password.superuser` / `.replication` | Auto-generated DB creds when not supplied (sensitive) |

## Chosen mechanism: Patroni + etcd (not CNPG)

CNPG is a single-Kubernetes-cluster operator; its auto-failover and anti-split-brain guarantee are
bound to one k8s API server and its cross-site "replica cluster" is manual-promotion DR, not
auto-failover — and the DO primary isn't a k8s node. Patroni runs the same image on the DO Docker host
and the local node, and uses a real 3-member etcd DCS spanning both sites + a witness for a 2-of-3
majority. **Full justification and the quorum/split-brain analysis are in
[`deploy/ha/README.md`](../ha/README.md).**

pgvector (mandatory) comes from [`../postgres/Dockerfile.patroni`](../postgres/Dockerfile.patroni) —
build & push it, then set `patroni_image`.

## Prerequisites

1. **DigitalOcean API token** — `export DIGITALOCEAN_TOKEN=dop_v1_...` (or set `do_token`).
2. **SSH public key(s)** — to operate the DB (`patronictl`, `etcdctl`) over SSH.
3. **A running Headscale mesh** ([`deploy/terraform/`](../terraform/)) with the local cluster joined.
   Mint **two** pre-auth keys on the mesh server:
   ```bash
   ssh root@<mesh-server> 'cd /opt/placecontext && ./deploy/pctl mesh authkey --tenant <id>'   # ×2
   ```
   → `mesh_authkey` (primary), `witness_mesh_authkey` (witness).
4. **The Patroni image built & pushed** (see above) → `patroni_image`.

## Usage

```bash
cd deploy/terraform-app
export DIGITALOCEAN_TOKEN=dop_v1_...
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars      # ssh_public_keys, mesh_control_url, mesh_authkey, witness_mesh_authkey, patroni_image
terraform init && terraform apply
terraform output next_steps   # post-apply runbook (verify leader, then join the local standby)
```

Then bring up the local standby and point the app at the cluster — see
**[`deploy/ha/README.md`](../ha/README.md)** (`pctl db-ha-join`, `app-db-endpoints.yaml`, the Npgsql
multi-host connection string).

## MagicDNS names (must be stable & match everywhere)

etcd's `--initial-cluster` is built from three MagicDNS names; keep them consistent across the module
vars and how each node registers on the tailnet:

| Var | Default | Node |
|-----|---------|------|
| `mesh_hostname` | `placecontext-db-primary` | this droplet (primary) |
| `witness_mesh_hostname` | `placecontext-db-witness` | witness droplet |
| `replica_mesh_hostname` | `placecontext-db-local` | the local k3s node (deploy/ha/) |

## Credentials

`superuser_password` / `replication_password` auto-generate (special-char-free, connection-string
safe) when left unset. Read them back:

```bash
terraform output -raw superuser_password
terraform output -raw replication_password
terraform output connection_string_hint      # the exact Npgsql multi-host string (password redacted)
```

The `replicator` role is least-privilege (LOGIN + REPLICATION only); `pg_hba` restricts it and the
superuser to the tailnet CIDRs with `scram-sha-256`.

## Firewall / "tailnet-only" enforcement

The DO cloud firewall opens **only 22/80/443** to the public. It deliberately does **not** open 5432
(Postgres), 8008 (Patroni REST) or 2379/2380 (etcd): Tailscale traffic to those arrives
WireGuard-encrypted and is decrypted on `tailscale0`, bypassing the cloud firewall — and the services
bind the node's tailnet IP, so they're never on the public NIC. Belt and suspenders. Tighten SSH with
`ssh_allowed_cidrs`. 80/443 are open only for an *optional* co-located edge and are unused by the DB.

## Teardown

```bash
terraform destroy
```

State (`*.tfstate`) and `terraform.tfvars` are gitignored (they hold the token, mesh keys, and DB
passwords). After destroy, remove the droplets' nodes from Headscale if they linger (`headscale nodes
list|delete` on the mesh server). The **local standby** is separate — tear it down on the local node
(`docker compose -f /opt/placecontext-db-local/docker-compose.yml down -v`).
