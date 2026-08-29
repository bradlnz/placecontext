# PlaceContext deployment

## Local/lab

The public installer creates a single-host [k3s](https://k3s.io) cluster with
[k3d](https://k3d.io) for evaluation. k3s is a lightweight Kubernetes distribution; k3d runs it
inside Docker for a simple local installation:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

It verifies a source-free release bundle, installs the prerequisites, creates a local k3d cluster,
pulls the versioned multi-architecture
[PlaceContext image from GHCR](https://github.com/bradlnz/placecontext/pkgs/container/placecontext),
and starts the portal and optional local AI worker. This path is deliberately not HA and is not the
production path. Object storage is disabled unless an operator supplies the optional
`placecontext-object-store` secret.

AI shards can be installed independently:

```bash
curl -fsSL https://get.placecontext.io/install.sh | \
  bash -s -- --ai-shard --shard-index 0 --total-shards 2
```

Use the same private `--ai-token` for every worker and the controller.

## Production on Proxmox/k3s

The production installer deploys only stateless PlaceContext resources to an existing cluster. It
fails closed unless the image is digest-pinned, TLS exists, all nodes are Ready, there are at least
three k3s server nodes and at least one worker node.

Provision these first:

- three k3s server VMs spread across independent Proxmox hosts/failure domains, using embedded etcd;
- a stable API VIP or load balancer and an odd number of servers;
- one or more worker VMs for untrusted Jobs (Jobs are refused on control-plane nodes);
- external HA PostgreSQL with automated PITR backups, off-site retention, and restore tests;
- off-cluster HTTPS S3 with encryption, versioning/lifecycle policy, and separate credentials;
- a TLS certificate secret and a secrets controller or equivalent audited secret delivery;
- off-cluster encrypted k3s/etcd snapshots and monitoring/alerting.
- a pinned, supported k3s release plus a tested patch/upgrade cadence.

A three-VM cluster on one physical Proxmox host is still one failure domain.

Before installing k3s, merge [`hardening/config.yaml`](k3s/hardening/config.yaml) into every server's
`/etc/rancher/k3s/config.yaml` and [`hardening/agent-config.yaml`](k3s/hardening/agent-config.yaml)
into every worker's config. Place the admission/audit/rate-limit files under
`/var/lib/rancher/k3s/server/`, install `90-kubelet.conf` under `/etc/sysctl.d/`, and run
`sysctl --system`. Add each node's cluster-init/server/token/TLS-SAN settings separately. These files
enable secrets encryption, audit logging, NodeRestriction, event rate limits, Pod Security admission,
kernel-default enforcement, and restricted TLS suites.

Create or sync these secrets in namespace `placecontext` before deployment:

| Secret | Required keys |
| --- | --- |
| `placecontext-db` | `connection-string` (external PostgreSQL) |
| `placecontext-object-store` | `endpoint`, `ACCESS_KEY_ID`, `ACCESS_SECRET_KEY`, `region`, `force-path-style`, `reports-bucket`, `deps-bucket` |
| `placecontext-portal` | `signing-key` |
| `placecontext-oauth` | `key.pem` |
| `placecontext-dp` | `key` |
| `placecontext-ca` | `tls.crt`, `tls.key` |
| your TLS secret | `tls.crt`, `tls.key` |

The object-store endpoint must be HTTPS. Do not commit plaintext Secret manifests. Configure SSO,
trusted OAuth clients, image pull credentials, DNS, and any external AI provider for the environment.

Every release publishes a `PRODUCTION_IMAGE` file containing the immutable multi-architecture GHCR
digest. Deploy from the verified release bundle:

```bash
./install-production.sh \
  --image ghcr.io/OWNER/REPO@sha256:DIGEST \
  --hostname placecontext.example.com \
  --tls-secret placecontext-tls
```

The deploy creates two trust zones: `placecontext` for the platform and `placecontext-jobs` for
untrusted code. Restricted Pod Security, least-privilege cross-namespace RBAC, per-job egress policy,
two portal replicas, topology spreading, a disruption budget, NetworkPolicies, and TLS-only Traefik
ingress are applied by default.
