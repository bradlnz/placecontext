# PlaceContext deployment

## Local/lab

The public installer creates a single-host [k3s](https://k3s.io) cluster with
[k3d](https://k3d.io) for evaluation. k3s is a lightweight Kubernetes distribution; k3d runs it
inside Docker for a simple local installation:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

It verifies a source-free release bundle, provisions Docker with passwordless `sudo` when needed on
Linux, installs the remaining tools in user space, creates a local
k3d cluster, pulls the versioned multi-architecture
[PlaceContext image from GHCR](https://github.com/bradlnz/placecontext/pkgs/container/placecontext),
and starts the portal. Configure AI providers in **Settings → Models**. The bundle does not include
an inference worker. This path is deliberately not HA and is not the
production path. Object storage is disabled unless an operator supplies the optional
`placecontext-object-store` secret.

When upgrading an installation that used bundled AI, stop its old `placecontext-ai` user service
or `io.placecontext.ai-worker` launch agent and remove the `placecontext-cluster-host` Kubernetes
deployment/service separately. The new release does not uninstall existing services or model files.
Saved model connections and indexed data are retained; new embeddings require a configured provider.

## Production on Kubernetes

The production installer deploys only stateless PlaceContext resources to an existing cluster. It
fails closed unless the image is digest-pinned, TLS exists, all nodes are Ready, there are at least
three visible control-plane nodes and at least one worker node. For managed clusters (including DigitalOcean Kubernetes), pass `--managed-control-plane`; the provider owns control-plane availability.

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

The platform runs in `placecontext`. Each tenant is provisioned into `pc-tenant-<tenant-id>` with
restricted Pod Security, a token-free workload service account, namespace-scoped management RBAC,
a resource quota, and default-deny ingress and egress. Per-run policies allow only the requested
outbound access. Dependency caches are tenant-scoped. Tenant Jobs use the shared node pool selected
by `placecontext-pool=shared`; label that pool before installation. Persistent shared workers are
not used for tenant jobs. Existing shared-namespace jobs must finish before this upgrade; the new
runner never falls back to the old namespace or cancels another tenant's jobs.

The application uses Kubernetes, PostgreSQL, and S3-compatible storage. Provider-specific provisioning
stays outside application code. The manifests under `k3s/` use standard Kubernetes APIs; the historical
directory name does not require k3s. Kubernetes 1.30+, enforced NetworkPolicy, Traefik in `kube-system`,
and metrics-server are required. Custom ingress installations must adapt the platform ingress policy.

### DigitalOcean first, portable runtime

Use DOKS for the managed control plane, managed PostgreSQL, and Spaces (or another S3-compatible store).
Create a shared worker pool with the `placecontext-pool=shared` label and autoscaling enabled. For example,
using an existing cluster and your chosen region-supported node size:

```bash
doctl kubernetes cluster node-pool create CLUSTER_ID \
  --name shared --size NODE_SIZE --count 1 \
  --label placecontext-pool=shared --auto-scale --min-nodes 1 --max-nodes 10
```

Run the production installer above with `--managed-control-plane`. Keep the platform's own nodes separate
from the labelled workload pool. The included HPA scales the portal from 2 to 10 replicas using CPU demand.
DOKS autoscaling adds worker nodes when resource requests cannot be scheduled and removes unused capacity;
registered user counts alone are not a capacity signal. Set the maximum pool size to your operating budget.
See [DigitalOcean node-pool autoscaling](https://docs.digitalocean.com/products/kubernetes/how-to/autoscale/).
On another cloud, use its node autoscaler with the same pool label and apply the same application manifests.
No provider API credentials are required by the application.

These controls establish tenant boundaries, but do not certify complete isolation. Validate cross-tenant
network access, storage access, and workload escape controls on the target cluster before accepting
untrusted customers. Namespaces share a kernel on ordinary container nodes; use a sandboxed runtime or
stronger infrastructure isolation when the threat model requires it. Host-side QEMU automation requires
a separately secured execution host and is not made namespaced by these changes.
