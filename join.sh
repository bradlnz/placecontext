#!/usr/bin/env bash
set -euo pipefail

JOIN_CODE="${JOIN_CODE:-}"
PORTAL="${PORTAL:-}"
TOKEN="${TOKEN:-}"
K3S_IMAGE="${K3S_IMAGE:-rancher/k3s:v1.31.5-k3s1}"
TS_CONTAINER="${TS_CONTAINER:-placecontext-tailscale}"
AGENT_CONTAINER="${AGENT_CONTAINER:-placecontext-agent}"
APP_IMAGE="${APP_IMAGE:-ghcr.io/bradlnz/placecontext:latest}"
NODE_TYPE="${NODE_TYPE:-standard-worker}"
DOCKER="${DOCKER:-docker}"

log()  { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✓\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m✗\033[0m %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

decode_join_code() {
  local code="$1"
  code="$(printf '%s' "$code" | tr -d '[:space:]')"
  case "$code" in PC1.*|PC2.*) ;; *) die "unrecognised join code (expected PC1.… or PC2.…)";; esac
  local b64="${code#PC?.}"
  local l s
  s="$(printf '%s' "$b64" | sed 'y#-_#+/#')"
  while [ $((${#s} % 4)) -ne 0 ]; do s="${s}="; done
  l="$(printf '%s' "$s" | base64 -d 2>/dev/null)" || l="$(printf '%s' "$s" | base64 -D 2>/dev/null)" || die "join code corrupted"
  local url="${l%% *}"
  local rest="${l#"$url"}"; rest="${rest# }"
  local token="${rest%% *}"
  local tskey="${rest#"$token"}"; tskey="${tskey# }"
  [ -n "$url" ] && [ -n "$token" ] || die "join code incomplete"
  printf '%s\n' "$url" "$token" "${tskey:-}"
}

url_host() {
  printf '%s' "$1" | sed -E 's#^https?://##; s#/.*$##; s#:.*$##; s/^\[//; s/\]$//'
}

install_docker() {
  if have docker; then
    if docker info >/dev/null 2>&1; then
      ok "Docker already installed and running"
      return 0
    fi
    warn "Docker found but not running — starting..."
    case "$(uname -s)" in
      Darwin) open -a Docker 2>/dev/null || true ;;
      Linux)  sudo systemctl start docker 2>/dev/null || true ;;
    esac
    for i in $(seq 1 30); do
      docker info >/dev/null 2>&1 && { ok "Docker started"; return 0; }
      sleep 2
    done
    die "Docker installed but won't start"
  fi
  log "Installing Docker..."
  case "$(uname -s)" in
    Darwin)
      if have brew; then
        brew install --cask docker
      else
        die "Install Docker Desktop from https://docs.docker.com/desktop/setup/install/mac-install/ then re-run"
      fi
      open -a Docker 2>/dev/null || true
      for i in $(seq 1 60); do
        docker info >/dev/null 2>&1 && { ok "Docker installed"; return 0; }
        sleep 3
      done
      die "Docker Desktop didn't start"
      ;;
    Linux)
      if have apt-get; then
        sudo apt-get update -qq && sudo apt-get install -y -qq ca-certificates curl
        sudo install -m 0755 -d /etc/apt/keyrings
        sudo curl -fsSL https://download.docker.com/linux/$(. /etc/os-release && echo "$ID")/gpg -o /etc/apt/keyrings/docker.asc
        sudo chmod a+r /etc/apt/keyrings/docker.asc
        printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' \
          "$(dpkg --print-architecture)" "$(. /etc/os-release && echo "$ID")" "$(. /etc/os-release && echo "$VERSION_CODENAME")" \
          | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
        sudo apt-get update -qq && sudo apt-get install -y -qq docker-ce docker-ce-cli containerd.io
      elif have dnf; then
        sudo dnf -y install dnf-plugins-core
        sudo dnf config-manager --add-repo https://download.docker.com/linux/$(. /etc/os-release && echo "$ID")/docker-ce.repo
        sudo dnf -y install docker-ce docker-ce-cli containerd.io
      elif have yum; then
        sudo yum install -y yum-utils
        sudo yum-config-manager --add-repo https://download.docker.com/linux/centos/docker-ce.repo
        sudo yum install -y docker-ce docker-ce-cli containerd.io
      else
        die "Unsupported distro — install Docker manually: https://docs.docker.com/engine/install/"
      fi
      sudo systemctl enable docker && sudo systemctl start docker
      sudo usermod -aG docker "$USER" 2>/dev/null || true
      if ! docker info >/dev/null 2>&1; then
        warn "User not in docker group yet — using sudo for docker commands"
        DOCKER="sudo docker"
      fi
      ok "Docker installed and running"
      ;;
    *) die "Unsupported OS: $(uname -s)" ;;
  esac
}

# ─── Parse args ──────────────────────────────────────────────────────────────
while [ $# -gt 0 ]; do
  case "$1" in
    --code)   JOIN_CODE="$2"; shift 2;;
    --portal) PORTAL="$2"; shift 2;;
    --token)  TOKEN="$2"; shift 2;;
    --node-type) NODE_TYPE="$2"; shift 2;;
    PC1.*|PC2.*) JOIN_CODE="$1"; shift;;
    *) die "Usage: $0 [--code PC2.xxxxx | --portal URL --token TOKEN | PC2.xxxxx]";;
  esac
done

case "$NODE_TYPE" in
  standard-worker|ai-shard) ;;
  *) die "Unsupported node type: $NODE_TYPE (expected standard-worker or ai-shard)" ;;
esac

# Exchange a one-time agent token for a real join code via the portal API.
if [ -n "$TOKEN" ]; then
  [ -z "$PORTAL" ] && die "--token requires --portal <portal-url>"
  have curl || die "curl is required for token exchange"
  log "Exchanging agent token for join code with $PORTAL..."
  EXCHANGE="$(curl -fsSL -H 'Content-Type: application/json' \
    -d "{\"token\":\"$TOKEN\"}" \
    "$PORTAL/api/v1/agent/exchange" 2>/dev/null)" || die "Token exchange failed — invalid or expired token?"
  JOIN_CODE="$(printf '%s' "$EXCHANGE" | sed -n 's/.*"joinCode":"\([^"]*\)".*/\1/p')"
  [ -z "$JOIN_CODE" ] && die "Token exchange succeeded but no join code returned"
  ok "Token accepted, join code obtained"
fi

[ -z "$JOIN_CODE" ] && die "Usage: $0 [--code PC2.xxxxx | --portal URL --token TOKEN | PC2.xxxxx]"

log "Decoding join code..."
read -r JOIN_URL JOIN_TOKEN JOIN_TSKEY <<< "$(decode_join_code "$JOIN_CODE")"
JOIN_HOST="$(url_host "$JOIN_URL")"
log "Server: $JOIN_URL"
log "Node token: ${#JOIN_TOKEN} chars"
if [ -n "$JOIN_TSKEY" ]; then
  ok "Tailscale auth key embedded in join code"
else
  warn "No Tailscale auth key in join code — host Tailscale required"
fi

install_docker

log "Cleaning any prior agent containers..."
$DOCKER rm -f "$AGENT_CONTAINER" "$TS_CONTAINER" >/dev/null 2>&1 || true

# ─── Tailscale ───────────────────────────────────────────────────────────────
AGENT_ARGS=()
AGENT_ARGS+=(--node-label="placecontext.io/node-type=$NODE_TYPE")
if [ -n "$JOIN_TSKEY" ]; then
  TS_HOST="$(hostname 2>/dev/null | cut -d. -f1 || echo pc)-pc"
  log "Starting Tailscale sidecar..."
  $DOCKER run -d --name "$TS_CONTAINER" --restart unless-stopped \
    --hostname "$TS_HOST" \
    --cap-add NET_ADMIN --cap-add NET_RAW --device /dev/net/tun \
    -e TS_AUTHKEY="$JOIN_TSKEY" \
    -e TS_STATE_DIR=/var/lib/tailscale \
    -e TS_USERSPACE=false \
    -e TS_EXTRA_ARGS=--accept-routes \
    -v placecontext-tailscale:/var/lib/tailscale \
    tailscale/tailscale:stable >/dev/null

  TS_IP=""
  for i in $(seq 1 30); do
    TS_IP="$($DOCKER exec "$TS_CONTAINER" tailscale ip -4 2>/dev/null | head -1 | tr -d '[:space:]')"
    [ -n "$TS_IP" ] && break
    [ $((i % 5)) -eq 0 ] && log "Waiting for Tailscale IP... ($i/30)"
    sleep 2
  done
  [ -z "$TS_IP" ] && die "Tailscale sidecar never got a mesh IP — check auth key"
  ok "Tailscale mesh IP: $TS_IP"
  export AGENT_NET=("--network=container:$TS_CONTAINER")
  AGENT_ARGS+=(--flannel-iface=tailscale0 --node-ip="$TS_IP")
else
  warn "No Tailscale auth key — the agent will use host networking"
  warn "Ensure this machine is already on the same tailnet as the cluster master"
  export AGENT_NET=("--network=host")
fi

# ─── k3s Agent ───────────────────────────────────────────────────────────────
log "Starting k3s agent container..."
$DOCKER pull "$K3S_IMAGE" >/dev/null 2>&1 &
PULL_PID=$!

# Wait for pull without blocking the whole script
wait "$PULL_PID" 2>/dev/null || true

$DOCKER run -d --name "$AGENT_CONTAINER" --privileged --restart unless-stopped \
  "${AGENT_NET[@]}" \
  -e K3S_URL="$JOIN_URL" -e K3S_TOKEN="$JOIN_TOKEN" \
  -v placecontext-agent-k3s:/var/lib/rancher/k3s \
  -v placecontext-agent-kubelet:/var/lib/kubelet \
  "$K3S_IMAGE" agent "${AGENT_ARGS[@]}" >/dev/null

# ─── App image ───────────────────────────────────────────────────────────────
log "Pulling app image in the agent (background)..."
$DOCKER exec -d "$AGENT_CONTAINER" \
  k3s ctr images pull "$APP_IMAGE" 2>/dev/null || true

# ─── Verify ──────────────────────────────────────────────────────────────────
log "Waiting for agent to register..."
for i in $(seq 1 30); do
  STATUS="$($DOCKER inspect "$AGENT_CONTAINER" --format='{{.State.Status}}' 2>/dev/null || true)"
  [ "$STATUS" = "running" ] || { sleep 2; continue; }
  LOGS="$($DOCKER logs "$AGENT_CONTAINER" --tail 5 2>/dev/null | head -1 || true)"
  if printf '%s' "$LOGS" | grep -qi "successfully" || [ $i -gt 20 ]; then
    ok "Agent container running and connected"
    break
  fi
  [ $((i % 5)) -eq 0 ] && log "Startup in progress... ($i/30)"
  sleep 2
done

cat <<EOF

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✅ Node joined successfully
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Server:   $JOIN_URL
  Host:     $JOIN_HOST

  Containers:
    $TS_CONTAINER    (Tailscale sidecar)
    $AGENT_CONTAINER  (k3s agent)

  On the master, verify:
    kubectl get nodes -o wide

  Manage this node:
    logs:  docker logs -f $AGENT_CONTAINER
    stop:  docker rm -f $AGENT_CONTAINER $TS_CONTAINER
    restart: docker start $TS_CONTAINER && docker start $AGENT_CONTAINER
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
EOF
