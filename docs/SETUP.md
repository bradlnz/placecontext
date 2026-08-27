# PlaceContext setup

PlaceContext ships one local installer and one deployment bundle. The bundle contains the k3s
manifests, the .NET AI shard coordinator, and the platform-native inference worker.

## Prerequisites

- Docker, running locally
- Python 3.11 or newer
- `curl`, `tar`, and OpenSSL

The installer downloads private copies of `k3d` and `kubectl`; they do not need to be installed
system-wide.

## Install locally

```bash
curl -fsSL https://raw.githubusercontent.com/bradlnz/placecontext/main/deploy/release/install.sh | bash
```

This command verifies the latest GitHub release checksum, creates a local k3s cluster in Docker,
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
curl -fsSL https://raw.githubusercontent.com/bradlnz/placecontext/main/deploy/release/install.sh | \
  bash -s -- --ai-shard --shard-index 0 --total-shards 2
```

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
2. builds and pushes the multi-architecture runtime image to GHCR;
3. substitutes the immutable image digest into the deployment configuration;
4. publishes stable and versioned archives plus `SHA256SUMS` to GitHub Releases.

The release source lives under [`deploy/release`](../deploy/release/).

## Data and secrets

The installer preserves existing Kubernetes Secrets on repeat runs. Back up PostgreSQL, MinIO,
configuration exports, and deployment secrets independently. Never commit populated environment
files, Kubernetes Secrets, private keys, passwords, or Vault exports.
