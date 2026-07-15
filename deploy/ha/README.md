# PlaceContext — cross-site auto-failover Postgres (DO primary ⇄ local replica)

Runs the `placecontext` database as a **2-site, auto-failover HA cluster**:

- **PRIMARY** (read-write leader) on a **DigitalOcean droplet** — provisioned by
  [`deploy/terraform-app/`](../terraform-app/).
- **Hot-standby REPLICA(s)** on the **local k3s node(s)** — brought up by
  [`join-local-replica.sh`](./join-local-replica.sh) / `pctl db-ha-join`.
- **WITNESS** (tiebreaker) on a small second DO droplet in a **different region** — provisioned by
  `deploy/terraform-app/` (`enable_witness = true`).

Replication is **asynchronous** and streams over the **Tailscale/Headscale mesh** (private tailnet
IPs — never the public internet). If DO goes offline the database stays **readable and writable
locally**: Patroni auto-promotes the local standby. When DO returns it rejoins as a standby.

```
                              ┌───────────────────────────────────────────┐
        Internet (SSH/edge) ─▶│  DigitalOcean droplet  ── PRIMARY          │
                              │    Patroni + PostgreSQL 16 (pgvector)      │  read-write leader
                              │    etcd member #1                          │
                              │    Tailscale ─ joins the mesh              │
                              └───────────────┬───────────────────────────┘
                                              │
              async streaming replication     │   (tailnet ONLY — WireGuard-encrypted)
              + etcd peer/client traffic       │
                                Headscale / Tailscale mesh
              ┌───────────────────────────────┼───────────────────────────────┐
              ▼                                                                ▼
  ┌──────────────────────────────────┐                    ┌──────────────────────────────────┐
  │ LOCAL k3s node                    │                    │ WITNESS droplet (other region)    │
  │   Patroni + PostgreSQL hot-standby│                    │   etcd member #3 (tiebreaker)     │
  │   etcd member #2                  │                    │   NO Postgres                     │
  │   PlaceContext app pods reach the │                    └──────────────────────────────────┘
  │   DB over the tailnet             │
  └──────────────────────────────────┘
```

## Why Patroni + etcd (not CloudNativePG)

The repo already ships a CloudNativePG (CNPG) path — [`deploy/k3s/postgres-ha.yaml`](../k3s/postgres-ha.yaml),
`pctl db ha`. **That path is kept for the single-cluster case** (1 primary + 2 replicas all inside one
k3s cluster). It is **not** the right tool for *this* task, because:

- **CNPG is a single-Kubernetes-cluster operator.** Its automatic failover and its anti-split-brain
  guarantee are anchored to **one** Kubernetes API server acting as the source of truth. That API
  server lives at one site. It has no notion of a third, cross-site quorum member.
- **CNPG's cross-site story is a *replica cluster* — manual-promotion DR, not auto-failover.** A CNPG
  "replica cluster" in a second Kubernetes cluster follows a *designated* primary that an operator
  changes by editing the spec. It will **not** auto-promote when the primary site dies. That fails the
  core requirement ("if DO goes offline the database is still accessible locally", automatically).
- **The DO primary host is not a Kubernetes node.** Making CNPG manage a Pod on DO would mean
  stretching one k3s cluster (its etcd/API server) across the WAN/mesh — fragile, and it moves the
  cluster's single quorum to one site anyway.

**Patroni + etcd fits cleanly:**

- Patroni runs the **same image** on a plain Docker host (the DO droplet) *and* on the local node — no
  requirement that every DB member be a Kubernetes node.
- The DCS is a **real 3-member etcd cluster spanning the two sites plus a witness**, giving an odd
  **2-of-3 majority**. This is exactly the consensus store a 2-site auto-failover cluster needs to not
  split-brain — see below.
- pgvector (mandatory) is provided by [`deploy/postgres/Dockerfile.patroni`](../postgres/Dockerfile.patroni),
  which installs `postgresql-16-pgvector` on top of `postgres:16`. `CREATE EXTENSION vector;` runs in
  the primary's `post_bootstrap` hook.

The user floated "Patroni or repmgr". Patroni was chosen over repmgr because Patroni's DCS-driven
leader lease is a stronger fencing primitive against split-brain than repmgr's witness+`repmgrd` model,
and it exposes a clean REST/`patronictl` control surface.

## Quorum & split-brain — the witness is mandatory

A 2-site auto-failover cluster with only two voting members **cannot** be made safe: if the sites
partition, each side sees "the other is gone" and both could promote → **two primaries, split-brain,
divergent data**. Two votes have no majority.

The fix is an **odd number of DCS members with an independent tiebreaker.** Here:

| Member                | Location                      | Role                    |
|-----------------------|-------------------------------|-------------------------|
| etcd #1               | DO droplet (primary)          | voter + hosts primary   |
| etcd #2               | local k3s node                | voter + hosts standby   |
| etcd #3 (**witness**) | **2nd DO droplet, other region** | voter only (no Postgres) |

Patroni only holds/keeps the leader lease while its side can **write to a majority of etcd (2 of 3)**.

- **DO site down / partitioned away:** local node + witness = 2/3 → majority. Patroni **auto-promotes
  the local standby**. The isolated DO side has only 1/3, loses its lease, and **demotes itself** — it
  cannot stay primary. No split-brain.
- **Local site down:** DO + witness = 2/3 → DO keeps the lease and stays primary.
- **Witness down (only):** DO + local = 2/3 → cluster keeps running normally; you've just lost the
  spare vote, so fix the witness before a second failure.

Put the **witness in a different region than the primary** (`witness_region != region`) so one
region-wide outage can't remove two votes at once. If you set `enable_witness = false` you **must**
supply a third etcd member elsewhere on the mesh, or the cluster has no majority and **will**
split-brain — do not run 2-site auto-failover without a third vote.

## Async replication & the data-loss window (be honest about this)

Replication is **asynchronous** (`synchronous_mode: false`). The primary commits and acks the client
**without** waiting for the standby. This is deliberate: synchronous replication over the Tailscale
mesh would make every DO write block on tailnet round-trips, and a slow/oscillating local link would
stall the primary. Async keeps DO fast and the local side available.

**Trade-off:** on an **unclean** primary failure (DO dies hard), any transactions committed on DO but
**not yet streamed** to the local standby are lost when the standby is promoted. The window is
normally sub-second but is bounded only by replication lag, not zero. This is the accepted cost of
async + "stay available locally". If you need zero-RPO you'd switch to `synchronous_mode: true` with a
synchronous standby and accept that DO writes then depend on the mesh — a different trade-off. Patroni
uses `pg_rewind` (`use_pg_rewind: true`) so the old primary can rejoin without a full re-clone after
its timeline diverges.

## App connection string (no app-code change on failover)

Npgsql speaks **multi-host** natively. Set:

```
PlaceContext__ConnectionString =
  Host=placecontext-db-primary,placecontext-db-local;Port=5432;Database=placecontext;\
  Username=postgres;Password=<superuser_password>;Target Session Attributes=primary;Load Balance Hosts=false
```

`Target Session Attributes=primary` makes Npgsql probe each host (`pg_is_in_recovery()`) and open the
session against whichever is the **current read-write leader**. After a failover the old primary is
down or read-only and Npgsql transparently uses the promoted local node — **no config change, no
redeploy**. Get the password with `terraform -chdir=deploy/terraform-app output -raw superuser_password`.

**Round-robin DNS (the user's "DNS-wise it will round robin"):** the two `Host=` names above are
Headscale **MagicDNS** names, one per node — that *is* a round-robin set of endpoints, and it's the
reliable way for Npgsql's primary-targeting to work (it needs to reach each candidate). You can *also*
publish a single round-robin A-record name (`db_rr_dns_name` / `db_rr_dns_values` in terraform-app)
across the node tailnet IPs for humans/tools that just want one name — but keep the multi-host list in
the app string, because a single name with multiple A records does not let Npgsql reliably pick the
primary.

**How k3s app pods reach the DB:** in-cluster CoreDNS doesn't resolve MagicDNS, so
[`app-db-endpoints.yaml`](./app-db-endpoints.yaml) publishes each DB node as a headless Service backed
by a manual `Endpoints` pointing at that node's tailnet IP. The app uses the Service names
(`placecontext-db-primary`, `placecontext-db-local`) in the multi-host string above. Fill in the two
tailnet IPs and `kubectl apply` it.

## Bring-up order

1. **Mesh must exist** — the Headscale control server from [`deploy/terraform/`](../terraform/), and the
   local cluster already joined (`pctl server up` / `pctl join`).
2. **Build & push the Patroni image** (both sites pull it):
   ```bash
   docker build -f deploy/postgres/Dockerfile.patroni -t ghcr.io/bradlnz/placecontext-patroni:16-pgvector .
   docker push ghcr.io/bradlnz/placecontext-patroni:16-pgvector
   ```
3. **Provision the DO primary + witness:**
   ```bash
   cd deploy/terraform-app
   cp terraform.tfvars.example terraform.tfvars   # fill mesh keys, ssh key, image, etc.
   terraform init && terraform apply
   ```
   Mint the two mesh pre-auth keys on the mesh server: `pctl mesh authkey --tenant <id>` (one for the
   primary → `mesh_authkey`, one for the witness → `witness_mesh_authkey`).
4. **Join the local node as a standby** (on the local k3s node, already meshed as
   `placecontext-db-local`):
   ```bash
   REPLICATION_PASSWORD=$(terraform -chdir=deploy/terraform-app output -raw replication_password) \
   SUPERUSER_PASSWORD=$(terraform -chdir=deploy/terraform-app output -raw superuser_password) \
   sudo -E ./deploy/pctl db-ha-join
   ```
   The three etcd members form the DCS (state `new`, quorum once 2 are up); Patroni bootstraps the
   primary on DO and clones the local standby via `pg_basebackup` over the tailnet.
5. **Point the app at the cluster:** edit tailnet IPs in `deploy/ha/app-db-endpoints.yaml`,
   `kubectl -n placecontext apply -f` it, then `kubectl set env` the multi-host connection string
   (see above).

> The MagicDNS names must be stable and match on every node: primary =
> `placecontext-db-primary`, local = `placecontext-db-local`, witness = `placecontext-db-witness`
> (the etcd `--initial-cluster` is built from them). Register each node with the matching
> `--hostname`.

## Verify replication & leadership

On any DB node (primary shown):

```bash
docker compose -f /opt/placecontext-db/docker-compose.yml exec -T patroni \
  patronictl -c /etc/patroni/patroni.yml list
```

Expected — one `Leader` (running) and the other(s) `Replica` in `State: streaming` with small `Lag in
MB`:

```
+ Cluster: placecontext (7...) --+---------+----+-----------+
| Member                  | Host | Role    | State     | TL | Lag in MB |
+-------------------------+------+---------+-----------+----+-----------+
| placecontext-db-primary | ...  | Leader  | running   |  1 |           |
| placecontext-db-local   | ...  | Replica | streaming |  1 |         0 |
+-------------------------+------+---------+-----------+----+-----------+
```

Check the DCS quorum from any node:
```bash
docker exec -it <etcd-container> etcdctl --endpoints=http://127.0.0.1:2379 endpoint status --cluster -w table
docker exec -it <etcd-container> etcdctl member list -w table   # expect 3 members, started
```

## Failover (DO primary lost)

**Automatic** — no action needed:
1. DO becomes unreachable. The DO Patroni can't reach a majority of etcd → releases/loses its leader
   lease and (if the box is up) demotes itself to read-only. It **cannot** stay primary.
2. Local node + witness hold 2/3 → Patroni promotes `placecontext-db-local` to leader (`pg_promote`).
3. Npgsql (`Target Session Attributes=primary`) reconnects to the promoted local node on its next
   attempt. Writes resume locally. **Data-loss window:** any un-streamed DO commits (async caveat).

Confirm: `patronictl list` on the local node now shows it as `Leader`.

## Failback (DO returns)

Patroni rejoins the old primary **as a standby automatically** — you do **not** manually re-promote:
1. The DO droplet/Patroni comes back, rejoins etcd, sees a newer leader on a higher timeline.
2. Patroni runs **`pg_rewind`** to reconcile the old primary's diverged WAL, then it **streams from
   the current (local) leader** as a standby. Verify with `patronictl list` (DO now `Replica /
   streaming`).
3. **Optional — move the leader back to DO** (planned, do it during a quiet window; there's a brief
   write pause):
   ```bash
   patronictl -c /etc/patroni/patroni.yml switchover --candidate placecontext-db-primary
   ```
   Only switch back once DO shows `State: streaming` with `Lag 0`. Leaving the leader local until a
   maintenance window is perfectly fine — the app follows the leader either way.

If the old primary's data volume was destroyed, delete its stale PGDATA and let Patroni re-clone
(`patronictl reinit placecontext placecontext-db-primary`).

## Security

- **Replication + DCS traffic is tailnet-only.** Services bind the node's tailnet IP and the DO cloud
  firewall never opens 5432 / 8008 / 2379 / 2380 publicly (tailnet traffic arrives decrypted on
  `tailscale0`, bypassing the cloud firewall entirely). Only SSH (tighten `ssh_allowed_cidrs`) and the
  optional edge 80/443 are public.
- **Least-privilege replication role** (`replicator`): `LOGIN` + `REPLICATION` only, no table access.
  `pg_hba` restricts it (and the superuser) to the tailnet CIDRs with `scram-sha-256`.
- **Secrets** are Terraform-sensitive vars, auto-generated when unset, and read back with
  `terraform output -raw`. They are never committed (`terraform.tfvars` + state are gitignored).

## What is NOT live-verifiable here

This was validated by **inspection + `terraform validate`** only — there is no real cluster/mesh in
this environment. Confirm on real infrastructure:

- **pgvector in the image:** `postgresql-16-pgvector` availability from PGDG and that `CREATE
  EXTENSION vector;` succeeds — verify by building `Dockerfile.patroni` and running the post-bootstrap.
- **Patroni ⇄ etcd v3** bootstrap across the mesh, and that all three MagicDNS names resolve on the
  tailnet before etcd can reach quorum (`initial-cluster-state: new` needs a majority reachable).
- **Auto-failover / auto-demote timing** and the actual async lag / data-loss window under your link.
- **`pg_rewind` failback** on a genuinely diverged timeline.
- **Npgsql `Target Session Attributes=primary`** re-targeting behavior with your Npgsql version and the
  headless-Service/Endpoints DNS path from the app pods.
