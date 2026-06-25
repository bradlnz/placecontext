#!/usr/bin/env bash
#
# selfhost.sh — stand up PlaceContext on k3s, gated by an activation key.
#
# What it does (run on the machine that will be the k3s server):
#   1. Installs k3s (server) if not already present.
#   2. Creates the `placecontext` namespace + an activation-key secret.
#   3. Deploys PostgreSQL (pgvector) and the PlaceContext Host (with activation enforced).
#   4. Prints the command to join additional worker nodes to the cluster.
#
# Usage:
#   sudo ./deploy/selfhost.sh --activation-key <KEY> [--image <IMG>]
#   # join a worker node (run on the worker, values printed by the server install):
#   sudo ./deploy/selfhost.sh --agent --server-url https://<server-ip>:6443 --node-token <TOKEN>
#
# Requires root (k3s installs a systemd service).
set -euo pipefail
cd "$(dirname "$0")/.."

IMAGE="ghcr.io/bradlnz/placecontext:latest"
ACTIVATION_KEY=""
NS="placecontext"
MODE="server"
SERVER_URL=""
NODE_TOKEN=""

while [ $# -gt 0 ]; do
  case "$1" in
    --activation-key) ACTIVATION_KEY="$2"; shift 2 ;;
    --image)          IMAGE="$2"; shift 2 ;;
    --agent)          MODE="agent"; shift ;;
    --server-url)     SERVER_URL="$2"; shift 2 ;;
    --node-token)     NODE_TOKEN="$2"; shift 2 ;;
    -h|--help)        grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; exit 2 ;;
  esac
done

say() { printf '\n\033[1;36m==>\033[0m %s\n' "$*"; }

# ── Worker (agent) node ─────────────────────────────────────────────────────────────────────────
if [ "$MODE" = "agent" ]; then
  [ -n "$SERVER_URL" ] && [ -n "$NODE_TOKEN" ] || { echo "--agent needs --server-url and --node-token" >&2; exit 2; }
  say "Joining this machine to the cluster as a worker node…"
  curl -sfL https://get.k3s.io | K3S_URL="$SERVER_URL" K3S_TOKEN="$NODE_TOKEN" sh -
  echo "Worker node joined."
  exit 0
fi

# ── Server node ─────────────────────────────────────────────────────────────────────────────────
[ -n "$ACTIVATION_KEY" ] || { echo "An activation key is required: --activation-key <KEY>" >&2; exit 2; }

say "Installing k3s (server)…"
if ! command -v k3s >/dev/null 2>&1; then
  curl -sfL https://get.k3s.io | sh -
else
  echo "    k3s already installed."
fi

KUBECTL="k3s kubectl"
say "Waiting for the cluster to be ready…"
until $KUBECTL get nodes >/dev/null 2>&1; do sleep 2; done

say "Creating namespace + activation secret…"
$KUBECTL create namespace "$NS" --dry-run=client -o yaml | $KUBECTL apply -f -
$KUBECTL -n "$NS" create secret generic placecontext-activation \
  --from-literal=key="$ACTIVATION_KEY" --dry-run=client -o yaml | $KUBECTL apply -f -

say "Deploying PostgreSQL (pgvector) + PlaceContext (image: ${IMAGE})…"
$KUBECTL -n "$NS" apply -f deploy/k3s/postgres.yaml
sed "s|__IMAGE__|${IMAGE}|g" deploy/k3s/placecontext.yaml | $KUBECTL -n "$NS" apply -f -

say "Done."
TOKEN_FILE="/var/lib/rancher/k3s/server/node-token"
cat <<EOF

PlaceContext is deploying to k3s (namespace: ${NS}).
  • Watch rollout:   k3s kubectl -n ${NS} rollout status deploy/placecontext
  • Portal/MCP:      via the Traefik ingress on this node (port 80)
  • Activation:      enforced — the secret holds your key; the portal shows status

Add a worker node — run this on the other machine:
  sudo ./deploy/selfhost.sh --agent \\
    --server-url https://$(hostname -I 2>/dev/null | awk '{print $1}'):6443 \\
    --node-token \$(sudo cat ${TOKEN_FILE})

EOF
