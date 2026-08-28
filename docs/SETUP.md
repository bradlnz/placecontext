# PlaceContext setup

PlaceContext ships one local installer and one deployment bundle. The bundle contains the k3s
manifests, the .NET AI shard coordinator, and the platform-native inference worker.

## Prerequisites

Start the installer with `curl` and a shell. It installs Docker through the host package manager
when necessary, installs Python 3.11+ for local AI, and downloads private copies of `k3d` and
`kubectl`. System package installation may prompt for `sudo`; on Linux, a newly-added Docker group
membership requires signing out and back in before rerunning the installer.

## Install locally

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

This command verifies the latest compiled release from Spaces, creates a local k3s cluster in Docker,
generates deployment secrets, starts a full-model local AI worker, and deploys PlaceContext at
<http://localhost:7700>.

Useful options:

```bash
# Pin a release or model
bash install.sh --version v1.2.3 --model Qwen/Qwen3.5-4B

# Use an existing ordered AI shard topology
bash install.sh --shard-endpoints http://100.64.0.10:8080,http://100.64.0.11:8080

# Deploy without local AI
bash install.sh --no-ai
```

## Add cluster capacity

Open **Cluster → Add node** in the portal and choose a node type.

- **Standard worker** joins k3s and runs ordinary PlaceContext jobs.
- **AI shard** joins with the `placecontext.io/node-type=ai-shard` label and installs an MLX/Torch
  inference worker. Set its zero-based shard index and the total shard count before copying the
  command.

The .NET `PlaceContext.ClusterHost` sequences AI shards in the configured endpoint order. Each AI
machine runs only the hardware inference boundary: MLX on Apple Silicon or Torch on Linux.

To install only an AI worker without the portal-generated join command:

```bash
curl -fsSL https://get.placecontext.io/install.sh | \
  bash -s -- --ai-shard --shard-index 0 --total-shards 2
```

The shard installer prints a controller token. Keep it private and pass the same value as
`--ai-token` when configuring the controller with `--shard-endpoints`; model APIs reject requests
without it, while health probes remain available for monitoring.

Then configure the controller with the worker URLs in shard order.

## Development from source

For portal development without k3d:

```bash
./setup.sh
./run.sh
```

Run the relevant checks before opening a change:

```bash
dotnet build PlaceContext.slnx
dotnet test PlaceContext.slnx
docker buildx build --check --platform linux/amd64,linux/arm64 .
```

## Release process

Push a `v*` tag. The release workflow:

1. tests the .NET shard coordinator;
2. builds architecture-specific runtime images from the private source;
3. packages each compiled image with the deployment files and checksums;
4. uploads the versioned bundles to DigitalOcean Spaces and updates `latest` last.

The release source lives under [`deploy/release`](../deploy/release/).

## Data and secrets

The installer preserves existing Kubernetes Secrets on repeat runs. Back up PostgreSQL, MinIO,
configuration exports, and deployment secrets independently. Never commit populated environment
files, Kubernetes Secrets, private keys, passwords, or Vault exports.
