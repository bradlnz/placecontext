# Cluster and nodes

*Check cluster health and add worker capacity.*

## Cluster page

Open **Cluster** to see:

- total and healthy node counts;
- control-plane and worker nodes;
- node address, platform, Kubernetes version, CPU, and memory;
- which node is the fleet master.

Use **Refresh** to update the view.

## Add a worker

Click **Add worker**. PlaceContext creates a short-lived join token and displays a one-time
command. Run that command in a terminal on the new machine.

Generate a new command for each worker. The worker joins the Kubernetes fleet and becomes
available for job execution.

The CLI can also create and use join codes:

```bash
# On the master
sudo placecontext join-code

# On the new machine
placecontext connect --code 'PC2.…'
```

Use `sudo` for a Linux system-service worker. Docker-based installs do not normally need it.

## Check the cluster

```bash
placecontext status
placecontext logs -f
placecontext doctor
placecontext url
```

Jobs run wherever the cluster scheduler has suitable capacity. A single-node installation works
without any workers.
