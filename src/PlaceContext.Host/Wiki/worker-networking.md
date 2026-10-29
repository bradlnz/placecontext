# Worker networking

*Define private networks and the traffic your jobs may use.*

Open **Networking → VPCs** in a project to create a VPC. Each VPC has one private IPv4 CIDR and
a default security group that blocks all traffic. VPC names and CIDRs are locked after creation.

Use the **Security groups** tab to create groups and filter by VPC. Select a table row to inspect
its **Inbound rules** and **Outbound rules** tables below. Use **Edit rules** to open the side pane.
Add allowed TCP or UDP traffic by CIDR and destination port range. Replies to allowed connections
are permitted. Empty groups block inbound and outbound connections.

Use **Job assignments** to choose a VPC and security group for each job. The assignment replaces
that job's network on/off setting for every shard and reduce step. Changes affect new runs and
replays; running jobs keep the policy captured when they started. Run details retain that policy.
Choose **Use job network setting** to remove an assignment.

To replace a VPC, remove its job assignments, then use **Delete** in the VPCs table. Deletion
requires confirmation and removes the saved VPC and its security groups. Running jobs retain
their captured policy. Provisioned cluster resources are retained so those runs can finish;
an operator must remove them once no running jobs use them. Creating a replacement gives it a new ID.

## Network flow and connection tests

Open **Network flow** and select a VPC and security group. The canvas shows inbound sources,
assigned jobs, outbound destinations, and the default block for unmatched connections. Select
a rule to fill in its direction, protocol, and first destination port in the test form.

Enter an IPv4 address and port, then use **Check rules** to identify matching saved rules.
This works for inbound and outbound TCP/UDP traffic without contacting a worker. It describes
the security-group decision, not end-to-end reachability. Matching rules are highlighted.

**Run outbound TCP test** starts a temporary worker with the selected VPC and security group,
including when no saved rule permits the connection. It attempts one TCP connection, waits up
to eight seconds, and sends no application data. It uses the normal worker scheduling and cleanup
path; startup plus execution is limited to two minutes. Cancel or leave the page to stop the test.
The Python runtime must be configured and available on the worker cluster.

Results distinguish a connection, refusal, timeout, unreachable destination, and a worker that
could not start. A timeout alone does not identify whether policy, routing, or the destination
caused it. A successful connection with no matching rule is flagged as unexpected; check other
Kubernetes policies and enforcement. Results show the tested target and configuration version.
Tests use saved rules; save edits before testing. They do not modify jobs or assignments.

The canvas is a configuration view, not live packet tracing. Live tests currently support outbound
TCP to unicast IPv4 addresses only; DNS, TLS/HTTP, inbound listeners, and UDP responses are not tested.

Saving a definition stores its configuration; it does not mean a network is running. The first
assigned job provisions the network and waits for it to be ready. An unsupported or unconfigured
worker rejects the job instead of running it without the requested restrictions.

## Administrator setup

VPC jobs use Kubernetes workers with [Kube-OVN](https://kubeovn.github.io/docs/stable/en/vpc/vpc/)
for private routing and address allocation. Security groups become per-run Kubernetes
NetworkPolicies, with both ingress and egress denied unless a rule allows them. Enable
NetworkPolicy enforcement in Kube-OVN. Do not add broad allow policies to the workload namespace;
Kubernetes combines allow rules from all policies that select a pod.

Prepare a Kube-OVN worker cluster, apply `deploy/release/k3s/worker-networking-rbac.yaml`, and set
`PlaceContext:WorkloadRunner:KubeOvnEnabled` to `true` on the host. The optional RBAC grants creation
and reading of VPCs and subnets; the usual workload permissions already cover NetworkPolicies.
Replacing a live cluster's network plugin can interrupt workloads and requires a planned migration.

Custom VPCs start isolated. DNS and internet access require operator-managed routing, a gateway,
and matching security-group rules. A rule for port 443 alone does not create an internet gateway.
See the [Kube-OVN VPC guide](https://kubeovn.github.io/docs/stable/en/vpc/vpc/) for gateway setup.
Address allocation spans workers in that Kubernetes cluster; separate clusters need separately
configured connectivity.

Docker and persistent native workers reject assigned VPC jobs. QEMU desktop steps currently run
on the workflow host with their own network toggle; these job assignments do not apply to them.
Networking administration requires Settings permission. Configuration backups include VPCs,
groups, and assignments; restoring them also requires Settings permission. Cluster resources and
operator-managed gateways need their own infrastructure backup. Imported jobs stay deny-egress
if restoration fails before saving their network assignment.

This first version supports one IPv4 subnet per VPC and one group per job. It does not manage
gateway provisioning, peering, ICMP rules, removal of individual security groups, or automatic
cleanup of provisioned cluster resources.
