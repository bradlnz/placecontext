# Cluster and nodes

*Check the k3s cluster health and add standard workers or local-AI shards.*

PlaceContext runs jobs on [k3s](https://k3s.io), a lightweight Kubernetes distribution. The local
installer creates it inside Docker with [k3d](https://k3d.io); larger installations use k3s directly
across server and worker nodes.

## Cluster page

Open **Settings → Cluster** (the existing **Cluster** shortcut also works) to see:

- total and healthy node counts;
- control-plane, standard-worker, and AI-shard nodes;
- node address, platform, Kubernetes version, CPU, and memory;
- which node is the fleet master.

Use **Refresh** to update the view.

## Common cluster layouts

PlaceContext does not require one kind of infrastructure. The control plane and workers can run on bare
metal, VMs, Macs, Linux servers, cloud hosts, or any mixture of them.

![PlaceContext spanning local and remote Mac, Linux, VM, and cloud workers](/images/cluster-layout.svg)

### Proxmox homelab

Run the portal on one always-on Linux VM, then add worker VMs sized for the jobs they run. A Mac or
another machine can join over Tailscale when it is outside the Proxmox LAN.

This is a useful homelab layout, but one physical Proxmox host is one failure domain. Production HA
uses three k3s server VMs spread across independent Proxmox hosts and separate worker VMs.

### Workers across sites with Tailscale

[Tailscale](https://tailscale.com/kb/1017/install) gives the master and workers stable private mesh
addresses even when they are behind different routers or NATs.

![Home and remote PlaceContext nodes connected through a Tailscale tailnet](/images/cluster-tailscale.svg)

Install Tailscale on the master first and join every host to the same tailnet. Joined nodes must be
able to reach the master's k3s API on TCP `6443` over the mesh. Tailscale provides connectivity; it
does not replace PlaceContext sign-in, permissions, TLS, or each job's network-egress policy.

## Add a node

Click **Add node** and choose one of two roles:

- **Standard worker** runs normal PlaceContext jobs and workload shards.
- **AI shard** runs an ordered slice of a local model through MLX on Apple Silicon or Torch on
  Linux. Select its zero-based shard index and the total number of shards.

PlaceContext creates a short-lived join token and displays a one-time command. Run that command in
a terminal on the new machine.

When the command contains a Tailscale auth key, `join.sh` starts a Tailscale sidecar and uses its mesh
address for the worker. Without an embedded key, install and connect Tailscale on the host before
running the command.

Generate a new command for each node. Kubernetes labels record
`placecontext.io/node-type=standard-worker` or `placecontext.io/node-type=ai-shard`, which drives
the role shown on this page.

## Check the cluster

Jobs run wherever the cluster scheduler has suitable capacity. A single-node installation works
without any workers.

## Cloud workers

For managed hosting, use the execution cluster supplied with your workspace and select a plan in
**Settings → Billing & usage**. For your own cloud infrastructure, deploy the control plane using
your production deployment configuration, open **Cluster → Add node**, and run the join command on
each cloud worker. Check the nodes are ready before submitting a test job.

## Job placement

The setup guide chooses instructions; it does not change scheduling or move existing workloads.
The portal's hosting location does not determine where jobs execute. Configure placement on each
execution host through the deployment's `PlaceContext:WorkloadRunner` settings.

For container jobs in a mixed cluster, label the intended workers and configure an exact selector:

```bash
kubectl label node YOUR_WORKER placecontext.io/execution-location=local --overwrite
```

```json
{
  "PlaceContext": {
    "WorkloadRunner": {
      "RequireWorkerNodes": true,
      "JobNodeSelector": {
        "placecontext.io/execution-location": "local"
      }
    }
  }
}
```

Use `cloud` for both the label and selector when jobs should run on cloud workers. Apply the settings
through your deployment and restart execution hosts to pick them up. A required worker selector
leaves jobs pending when no matching worker is available; it does not fall back to the portal's node.
For a single-node local cluster, leave `RequireWorkerNodes` false so its control-plane node can run jobs.

Persistent native runners and desktop/VM execution use their own configured hosts; the container
node selector does not relocate them. Verify those hosts separately if those execution modes are enabled.
Always verify placement with a test job. Ready nodes alone do not prove a workload can execute.
