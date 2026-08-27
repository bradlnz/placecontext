# Cluster and nodes

*Check cluster health and add standard workers or local-AI shards.*

## Cluster page

Open **Cluster** to see:

- total and healthy node counts;
- control-plane, standard-worker, and AI-shard nodes;
- node address, platform, Kubernetes version, CPU, and memory;
- which node is the fleet master.

Use **Refresh** to update the view.

## Add a node

Click **Add node** and choose one of two roles:

- **Standard worker** runs normal PlaceContext jobs and workload shards.
- **AI shard** runs an ordered slice of a local model through MLX on Apple Silicon or Torch on
  Linux. Select its zero-based shard index and the total number of shards.

PlaceContext creates a short-lived join token and displays a one-time command. Run that command in
a terminal on the new machine.

Generate a new command for each node. Kubernetes labels record
`placecontext.io/node-type=standard-worker` or `placecontext.io/node-type=ai-shard`, which drives
the role shown on this page.

## Check the cluster

Jobs run wherever the cluster scheduler has suitable capacity. A single-node installation works
without any workers.
