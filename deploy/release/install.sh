#!/usr/bin/env bash
# PlaceContext local release installer.
#
#   curl -fsSL https://raw.githubusercontent.com/bradlnz/placecontext/main/deploy/release/install.sh | bash
#
# The installer downloads the release, kubectl and k3d, starts a local inference
# worker, and applies the k3s manifests. Docker, Python 3.11+, curl, and OpenSSL
# are system prerequisites.
set -euo pipefail

REPOSITORY="${PLACECONTEXT_REPOSITORY:-bradlnz/placecontext}"
VERSION="${PLACECONTEXT_VERSION:-latest}"
INSTALL_DIR="${PLACECONTEXT_HOME:-$HOME/.local/share/placecontext}"
MODEL="${PLACECONTEXT_MODEL:-Qwen/Qwen3.5-4B}"
SHARD_ENDPOINTS="${PLACECONTEXT_SHARD_ENDPOINTS:-}"
PORT="${PLACECONTEXT_PORT:-7700}"
CLUSTER_NAME="${PLACECONTEXT_CLUSTER:-placecontext}"
NAMESPACE="${PLACECONTEXT_NAMESPACE:-placecontext}"
INSTALL_AI=1
AI_SHARD_ONLY=0
SHARD_INDEX=0
TOTAL_SHARDS=1
WAIT=1
FROM_BUNDLE="${PLACECONTEXT_FROM_BUNDLE:-0}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --shard-endpoints) SHARD_ENDPOINTS="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --ai-shard) AI_SHARD_ONLY=1; shift ;;
    --shard-index) SHARD_INDEX="$2"; shift 2 ;;
    --total-shards) TOTAL_SHARDS="$2"; shift 2 ;;
    --no-ai) INSTALL_AI=0; shift ;;
    --no-wait) WAIT=0; shift ;;
    --from-bundle) FROM_BUNDLE=1; shift ;;
    -h|--help)
      printf '%s\n' \
        'PlaceContext local release installer' \
        '' \
        'Usage:' \
        '  install.sh [--version TAG] [--model ID] [--shard-endpoints URL,URL]' \
        '' \
        'Options:' \
        '  --version TAG          GitHub release tag (default: latest)' \
        '  --install-dir DIR      Install location' \
        '  --model ID             Hugging Face model ID' \
        '  --shard-endpoints LIST Ordered comma-separated worker URLs' \
        '  --port PORT            Local portal port (default: 7700)' \
        '  --ai-shard            Install only an MLX/Torch AI shard worker' \
        '  --shard-index N       Zero-based shard index (default: 0)' \
        '  --total-shards N      Number of ordered shards (default: 1)' \
        '  --no-ai                Do not install the AI worker or coordinator' \
        '  --no-wait              Return before the Host rollout completes'
      exit 0
      ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done

[[ "$SHARD_INDEX" =~ ^[0-9]+$ ]] || { printf 'Invalid --shard-index: %s\n' "$SHARD_INDEX" >&2; exit 2; }
[[ "$TOTAL_SHARDS" =~ ^[1-9][0-9]*$ ]] || { printf 'Invalid --total-shards: %s\n' "$TOTAL_SHARDS" >&2; exit 2; }
(( SHARD_INDEX < TOTAL_SHARDS )) || { printf '%s\n' '--shard-index must be less than --total-shards' >&2; exit 2; }
[[ "$AI_SHARD_ONLY" == 0 ]] || INSTALL_AI=1

say() { printf '==> %s\n' "$*"; }
ok() { printf '  ✓ %s\n' "$*"; }
die() { printf '  ✗ %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

download() {
  curl -fsSL --retry 3 --retry-delay 2 "$1" -o "$2"
}

sha256_file() {
  if have sha256sum; then
    sha256sum "$1" | awk '{print $1}'
  elif have shasum; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    die "sha256sum or shasum is required"
  fi
}

verify_sha256() {
  local archive="$1" sums="$2" expected actual
  expected="$(awk '$2 == "placecontext-deploy.tar.gz" || $2 == "./placecontext-deploy.tar.gz" { print $1; exit }' "$sums")"
  [[ -n "$expected" ]] || die "release checksum is missing placecontext-deploy.tar.gz"
  actual="$(sha256_file "$archive")"
  [[ "$actual" == "$expected" ]] || die "release checksum verification failed"
}

bootstrap_release() {
  have curl || die "curl is required"
  have tar || die "tar is required"

  local tag_path tmp base bundle
  local -a install_args
  if [[ "$VERSION" == "latest" ]]; then
    tag_path="latest/download"
  else
    [[ "$VERSION" == v* ]] || VERSION="v$VERSION"
    tag_path="download/$VERSION"
  fi
  base="https://github.com/$REPOSITORY/releases/$tag_path"
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/placecontext.XXXXXX")"
  trap 'rm -rf "$tmp"' EXIT

  say "Downloading PlaceContext ${VERSION}"
  download "$base/placecontext-deploy.tar.gz" "$tmp/placecontext-deploy.tar.gz"
  download "$base/SHA256SUMS" "$tmp/SHA256SUMS"
  verify_sha256 "$tmp/placecontext-deploy.tar.gz" "$tmp/SHA256SUMS"
  tar -xzf "$tmp/placecontext-deploy.tar.gz" -C "$tmp"
  bundle="$tmp/placecontext-deploy"
  [[ -x "$bundle/install.sh" ]] || die "release does not contain install.sh"
  ok "release verified"

  install_args=(
    --version "$VERSION"
    --install-dir "$INSTALL_DIR"
    --model "$MODEL"
    --port "$PORT"
  )
  [[ "$INSTALL_AI" == 1 ]] || install_args+=(--no-ai)
  [[ "$AI_SHARD_ONLY" == 0 ]] || install_args+=(--ai-shard --shard-index "$SHARD_INDEX" --total-shards "$TOTAL_SHARDS")
  [[ "$WAIT" == 1 ]] || install_args+=(--no-wait)
  [[ -z "$SHARD_ENDPOINTS" ]] || install_args+=(--shard-endpoints "$SHARD_ENDPOINTS")
  PLACECONTEXT_FROM_BUNDLE=1 bash "$bundle/install.sh" "${install_args[@]}"
}

if [[ "$FROM_BUNDLE" != 1 ]]; then
  bootstrap_release
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ -d "$SCRIPT_DIR/k3s" && -f "$SCRIPT_DIR/local-ai/runtime.yaml" ]] \
  || die "installer must run from a PlaceContext release bundle"

mkdir -p "$INSTALL_DIR"
if [[ "$SCRIPT_DIR" != "$INSTALL_DIR" ]]; then
  cp -R "$SCRIPT_DIR/." "$INSTALL_DIR/"
fi
ROOT="$INSTALL_DIR"
mkdir -p "$ROOT/bin" "$ROOT/state" "$ROOT/logs"

detect_platform() {
  case "$(uname -s)" in
    Linux) TOOL_OS=linux ;;
    Darwin) TOOL_OS=darwin ;;
    *) die "only Linux and macOS are supported" ;;
  esac
  case "$(uname -m)" in
    x86_64|amd64) TOOL_ARCH=amd64 ;;
    arm64|aarch64) TOOL_ARCH=arm64 ;;
    *) die "unsupported CPU architecture: $(uname -m)" ;;
  esac
}

install_k3d() {
  have k3d && return
  local release="${K3D_VERSION:-v5.8.3}" name sums expected actual
  name="k3d-$TOOL_OS-$TOOL_ARCH"
  sums="$(mktemp "${TMPDIR:-/tmp}/k3d-checksums.XXXXXX")"
  say "Installing k3d $release"
  download "https://github.com/k3d-io/k3d/releases/download/$release/$name" "$ROOT/bin/k3d"
  download "https://github.com/k3d-io/k3d/releases/download/$release/checksums.txt" "$sums"
  expected="$(awk -v name="$name" '$2 == name {print $1; exit}' "$sums")"
  actual="$(sha256_file "$ROOT/bin/k3d")"
  rm -f "$sums"
  [[ -n "$expected" && "$actual" == "$expected" ]] || die "k3d checksum verification failed"
  chmod 0755 "$ROOT/bin/k3d"
}

install_kubectl() {
  have kubectl && return
  local stable binary checksum actual
  say "Installing kubectl"
  stable="$(curl -fsSL https://dl.k8s.io/release/stable.txt)"
  binary="$ROOT/bin/kubectl"
  download "https://dl.k8s.io/release/$stable/bin/$TOOL_OS/$TOOL_ARCH/kubectl" "$binary"
  checksum="$(curl -fsSL "https://dl.k8s.io/release/$stable/bin/$TOOL_OS/$TOOL_ARCH/kubectl.sha256")"
  actual="$(sha256_file "$binary")"
  [[ "$actual" == "$checksum" ]] || die "kubectl checksum verification failed"
  chmod 0755 "$binary"
}

ensure_secret() {
  local name="$1"; shift
  if ! kubectl -n "$NAMESPACE" get secret "$name" >/dev/null 2>&1; then
    kubectl -n "$NAMESPACE" create secret generic "$name" "$@" >/dev/null
  fi
}

random_hex() {
  openssl rand -hex "$1"
}

configure_secrets() {
  say "Configuring local secrets"
  local pg_password connection rsa_key cert_dir
  kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  ensure_secret placecontext-portal --from-literal="signing-key=$(random_hex 32)"
  ensure_secret placecontext-dp --from-literal="key=$(random_hex 32)"

  if ! kubectl -n "$NAMESPACE" get secret placecontext-oauth >/dev/null 2>&1; then
    rsa_key="$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null)"
    kubectl -n "$NAMESPACE" create secret generic placecontext-oauth \
      --from-literal="key.pem=$rsa_key" >/dev/null
  fi

  if ! kubectl -n "$NAMESPACE" get secret placecontext-db >/dev/null 2>&1; then
    pg_password="$(random_hex 24)"
    connection="Host=placecontext-db;Port=5432;Database=placecontext;Username=postgres;Password=$pg_password"
    kubectl -n "$NAMESPACE" create secret generic placecontext-db \
      --from-literal="password=$pg_password" \
      --from-literal="connection-string=$connection" >/dev/null
  fi
  ensure_secret placecontext-minio \
    --from-literal="ACCESS_KEY_ID=pc$(random_hex 4)" \
    --from-literal="ACCESS_SECRET_KEY=$(random_hex 24)"

  if ! kubectl -n "$NAMESPACE" get secret placecontext-ca >/dev/null 2>&1; then
    cert_dir="$(mktemp -d "${TMPDIR:-/tmp}/placecontext-ca.XXXXXX")"
    openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
      -subj '/CN=PlaceContext Local CA' \
      -keyout "$cert_dir/tls.key" -out "$cert_dir/tls.crt" >/dev/null 2>&1
    kubectl -n "$NAMESPACE" create secret tls placecontext-ca \
      --key "$cert_dir/tls.key" --cert "$cert_dir/tls.crt" >/dev/null
    rm -rf "$cert_dir"
  fi
  ok "secrets ready"
}

start_worker() {
  [[ "$INSTALL_AI" == 1 ]] || return
  [[ -z "$SHARD_ENDPOINTS" ]] || return
  have python3 || die "Python 3.11+ is required for the local inference worker"
  python3 -c 'import sys; raise SystemExit(sys.version_info < (3, 11))' \
    || die "Python 3.11+ is required for the local inference worker"

  local venv="$ROOT/ai/venv" worker="$ROOT/local-ai/worker.py" shard_spec="$SHARD_INDEX/$TOTAL_SHARDS"
  [[ -f "$worker" ]] || die "release does not contain the inference worker"
  mkdir -p "$ROOT/ai"
  if [[ ! -x "$venv/bin/python" ]]; then
    say "Creating local AI environment"
    python3 -m venv "$venv"
  fi
  "$venv/bin/python" -m pip install --quiet --upgrade pip
  if [[ "$TOOL_OS/$TOOL_ARCH" == "darwin/arm64" ]]; then
    "$venv/bin/python" -m pip install --quiet mlx-lm fastapi uvicorn pydantic numpy
  else
    "$venv/bin/python" -m pip install --quiet torch transformers accelerate safetensors fastapi uvicorn pydantic numpy
  fi

  say "Starting local AI worker"
  if [[ "$TOOL_OS" == darwin ]]; then
    local plist="$HOME/Library/LaunchAgents/io.placecontext.ai-worker.plist"
    mkdir -p "$(dirname "$plist")"
    sed \
      -e "s|__PYTHON__|$venv/bin/python|g" \
      -e "s|__WORKER__|$worker|g" \
      -e "s|__MODEL__|$MODEL|g" \
      -e "s|__SHARD_SPEC__|$shard_spec|g" \
      -e "s|__LOG_DIR__|$ROOT/logs|g" \
      "$ROOT/local-ai/io.placecontext.ai-worker.plist" > "$plist"
    launchctl bootout "gui/$(id -u)" "$plist" >/dev/null 2>&1 || true
    launchctl bootstrap "gui/$(id -u)" "$plist"
  elif have systemctl && systemctl --user show-environment >/dev/null 2>&1; then
    local unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
    mkdir -p "$unit_dir"
    sed \
      -e "s|__PYTHON__|$venv/bin/python|g" \
      -e "s|__WORKER__|$worker|g" \
      -e "s|__MODEL__|$MODEL|g" \
      -e "s|__SHARD_SPEC__|$shard_spec|g" \
      "$ROOT/local-ai/placecontext-ai.service" > "$unit_dir/placecontext-ai.service"
    systemctl --user daemon-reload
    systemctl --user enable --now placecontext-ai.service
  else
    SHARD_SPEC="$shard_spec" nohup "$venv/bin/python" "$worker" --model "$MODEL" --port 8080 \
      >"$ROOT/logs/ai-worker.log" 2>&1 &
    printf '%s\n' "$!" > "$ROOT/state/ai-worker.pid"
  fi
  SHARD_ENDPOINTS="http://host.k3d.internal:8080"
  ok "AI shard $shard_spec configured (model downloads on first start)"
}

configure_cluster_ai() {
  [[ "$INSTALL_AI" == 1 ]] || return
  local -a endpoints=()
  local endpoint index=0 args=()
  IFS=',' read -r -a endpoints <<< "$SHARD_ENDPOINTS"
  [[ "${#endpoints[@]}" -gt 0 && -n "${endpoints[0]}" ]] || die "no shard endpoints were configured"
  args+=(--from-literal="PlaceContext__ClusterChat__Model=$MODEL")
  for endpoint in "${endpoints[@]}"; do
    endpoint="${endpoint%/}"
    args+=(--from-literal="PlaceContext__ClusterChat__ShardEndpoints__${index}=$endpoint")
    index=$((index + 1))
  done
  kubectl -n "$NAMESPACE" create configmap placecontext-local-ai "${args[@]}" \
    --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  kubectl -n "$NAMESPACE" apply -f "$ROOT/local-ai/runtime.yaml" >/dev/null
  kubectl -n "$NAMESPACE" rollout restart deployment/placecontext-cluster-host >/dev/null
  ok ".NET shard coordinator configured (${#endpoints[@]} endpoint(s))"
}

deploy_local() {
  detect_platform
  if [[ "$AI_SHARD_ONLY" == 1 ]]; then
    start_worker
    ok "AI shard is listening on port 8080"
    printf '    Configure the controller with this machine\047s reachable URL.\n'
    return
  fi

  have docker || die "Docker must be installed and running"
  docker info >/dev/null 2>&1 || die "Docker is installed but not running"
  have openssl || die "openssl is required"
  export PATH="$ROOT/bin:$PATH"
  install_k3d
  install_kubectl

  say "Starting local k3s cluster"
  if k3d cluster list 2>/dev/null | awk 'NR > 1 {print $1}' | grep -qx "$CLUSTER_NAME"; then
    k3d cluster start "$CLUSTER_NAME" >/dev/null 2>&1 || true
  else
    k3d cluster create "$CLUSTER_NAME" --agents 1 \
      --port "$PORT:80@loadbalancer" --wait
  fi
  export KUBECONFIG
  KUBECONFIG="$(k3d kubeconfig write "$CLUSTER_NAME")"

  configure_secrets
  start_worker
  say "Applying PlaceContext"
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/postgres.yaml" >/dev/null
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/redis.yaml" >/dev/null
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/minio.yaml" >/dev/null
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/pg-backup.yaml" >/dev/null
  local runtime_image cluster_endpoint
  runtime_image="$(awk -F'"' '/runtime_image:/ {print $2; exit}' "$ROOT/local-ai/config.yaml")"
  cluster_endpoint="http://placecontext-cluster-host:8081/api/cluster"
  [[ "$INSTALL_AI" == 1 ]] || cluster_endpoint=""
  sed -e "s|__IMAGE__|$runtime_image|g" \
      -e 's|__IMAGE_PULL_POLICY__|IfNotPresent|g' \
      -e "s|value: \"http://placecontext-cluster-host:8081/api/cluster\"|value: \"$cluster_endpoint\"|g" \
      "$ROOT/k3s/placecontext.yaml" | kubectl -n "$NAMESPACE" apply -f - >/dev/null
  configure_cluster_ai

  if [[ "$WAIT" == 1 ]]; then
    say "Waiting for PlaceContext"
    kubectl -n "$NAMESPACE" rollout status deployment/placecontext --timeout=5m
  fi
  ok "PlaceContext is available at http://localhost:$PORT"
  printf '    Installed files: %s\n' "$ROOT"
}

deploy_local
