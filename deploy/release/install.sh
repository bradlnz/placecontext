#!/usr/bin/env bash
# PlaceContext local release installer.
#
#   curl -fsSL https://get.placecontext.io/install.sh | bash
#
# The GitHub release bundle contains deployment files only; the runtime is pulled from GHCR.
# The installer provisions its tools and Python environment, creates k3d, and applies the manifests.
set -euo pipefail

VERSION="${PLACECONTEXT_VERSION:-latest}"
BASE_URL="${PLACECONTEXT_BASE_URL:-https://github.com/bradlnz/placecontext/releases}"
INSTALL_DIR="${PLACECONTEXT_HOME:-$HOME/.local/share/placecontext}"
MODEL="${PLACECONTEXT_MODEL:-Qwen/Qwen3.5-4B}"
SHARD_ENDPOINTS="${PLACECONTEXT_SHARD_ENDPOINTS:-}"
AI_TOKEN="${PLACECONTEXT_AI_TOKEN:-}"
AI_TOKEN_PROVIDED=0
[[ -z "$AI_TOKEN" ]] || AI_TOKEN_PROVIDED=1
PORT="${PLACECONTEXT_PORT:-7700}"
CLUSTER_NAME="${PLACECONTEXT_CLUSTER:-placecontext-local}"
NAMESPACE="placecontext"
INSTALL_AI=1
AI_SHARD_ONLY=0
SHARD_INDEX=0
TOTAL_SHARDS=1
WAIT=1
FROM_BUNDLE="${PLACECONTEXT_FROM_BUNDLE:-0}"
ROOTLESS_DOCKER_INSTALLER_COMMIT="42dcae692436f34526524ed46d3b32885c9355f5"
ROOTLESS_DOCKER_INSTALLER_SHA256="519165a123f9924c530c64bdba3019124555eb311a671e149e2d1c1f79a6a92d"
ROOTLESS_DOCKER_VERSION="29.7.2"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --shard-endpoints) SHARD_ENDPOINTS="$2"; shift 2 ;;
    --ai-token) AI_TOKEN="$2"; AI_TOKEN_PROVIDED=1; shift 2 ;;
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
        'Installs without sudo; Linux uses Docker rootless mode when needed.' \
        '' \
        'Usage:' \
        '  install.sh [--version TAG] [--model ID] [--shard-endpoints URL,URL]' \
        '' \
        'Options:' \
        '  --version TAG          GitHub release version (default: latest)' \
        '  --install-dir DIR      Install location' \
        '  --model ID             Hugging Face model ID' \
        '  --shard-endpoints LIST Ordered comma-separated worker URLs' \
        '  --ai-token TOKEN       Shared controller/worker token for remote shards' \
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
[[ "$MODEL" =~ ^[A-Za-z0-9._/-]+$ ]] || { printf 'Invalid --model: %s\n' "$MODEL" >&2; exit 2; }
if [[ "$AI_TOKEN_PROVIDED" == 1 && ! "$AI_TOKEN" =~ ^[A-Za-z0-9._~-]{32,256}$ ]]; then
  printf '%s\n' 'Invalid --ai-token: expected 32-256 URL-safe characters' >&2
  exit 2
fi
[[ "$AI_SHARD_ONLY" == 0 ]] || INSTALL_AI=1

say() { printf '==> %s\n' "$*"; }
ok() { printf '  ✓ %s\n' "$*"; }
die() { printf '  ✗ %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

INSTALL_ARGS=()
build_install_args() {
  INSTALL_ARGS=(
    --version "$VERSION"
    --install-dir "$INSTALL_DIR"
    --model "$MODEL"
    --port "$PORT"
  )
  [[ "$INSTALL_AI" == 1 ]] || INSTALL_ARGS+=(--no-ai)
  [[ "$AI_SHARD_ONLY" == 0 ]] || INSTALL_ARGS+=(--ai-shard --shard-index "$SHARD_INDEX" --total-shards "$TOTAL_SHARDS")
  [[ "$WAIT" == 1 ]] || INSTALL_ARGS+=(--no-wait)
  [[ -z "$SHARD_ENDPOINTS" ]] || INSTALL_ARGS+=(--shard-endpoints "$SHARD_ENDPOINTS")
  [[ "$AI_TOKEN_PROVIDED" == 0 ]] || INSTALL_ARGS+=(--ai-token "$AI_TOKEN")
}

download() {
  curl -fsSL --retry 3 --retry-delay 2 "$1" -o "$2"
}

normalise_arch() {
  case "$(uname -m)" in
    x86_64|amd64) printf '%s\n' amd64 ;;
    arm64|aarch64) printf '%s\n' arm64 ;;
    *) die "unsupported CPU architecture: $(uname -m)" ;;
  esac
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
  local archive="$1" sums="$2" asset="$3" expected actual
  expected="$(awk -v asset="$asset" '$2 == asset || $2 == "./" asset { print $1; exit }' "$sums")"
  [[ -n "$expected" ]] || die "release checksum is missing $asset"
  actual="$(sha256_file "$archive")"
  [[ "$actual" == "$expected" ]] || die "release checksum verification failed"
}

validate_archive() {
  local archive="$1" entry listing
  listing="$(mktemp "${TMPDIR:-/tmp}/placecontext-tar.XXXXXX")"
  tar -tzf "$archive" > "$listing" || { rm -f "$listing"; die "release archive is invalid"; }
  while IFS= read -r entry; do
    case "$entry" in
      placecontext-deploy|placecontext-deploy/*) ;;
      *) rm -f "$listing"; die "release archive contains an unsafe path: $entry" ;;
    esac
    case "/$entry/" in
      */../*|*/./*) rm -f "$listing"; die "release archive contains traversal: $entry" ;;
    esac
  done < "$listing"
  rm -f "$listing"

  # Release bundles contain only directories and regular files. Reject links and special files so
  # extraction cannot redirect a later member outside the temporary bundle directory.
  if tar -tvzf "$archive" | awk 'substr($1,1,1) != "-" && substr($1,1,1) != "d" { exit 1 }'; then
    return
  fi
  die "release archive contains links or special files"
}

bootstrap_release() {
  have curl || die "curl is required"
  have tar || die "tar is required"

  local tmp bundle asset release_base version_file
  BASE_URL="${BASE_URL%/}"
  if [[ "$VERSION" == "latest" ]]; then
    release_base="$BASE_URL/latest/download"
    version_file="$(mktemp "${TMPDIR:-/tmp}/placecontext-version.XXXXXX")"
    download "$release_base/VERSION" "$version_file"
    VERSION="$(tr -d '[:space:]' < "$version_file")"
    rm -f "$version_file"
    [[ -n "$VERSION" ]] || die "latest release version is empty"
  else
    VERSION="${VERSION#v}"
    release_base="$BASE_URL/download/v$VERSION"
  fi
  asset="placecontext-deploy.tar.gz"
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/placecontext.XXXXXX")"
  trap "rm -rf -- $(printf '%q' "$tmp")" EXIT

  say "Downloading PlaceContext ${VERSION}"
  download "$release_base/$asset" "$tmp/$asset"
  download "$release_base/SHA256SUMS" "$tmp/SHA256SUMS"
  verify_sha256 "$tmp/$asset" "$tmp/SHA256SUMS" "$asset"
  validate_archive "$tmp/$asset"
  tar -xzf "$tmp/$asset" -C "$tmp"
  bundle="$tmp/placecontext-deploy"
  [[ -x "$bundle/install.sh" ]] || die "release does not contain install.sh"
  ok "release verified"

  build_install_args
  PLACECONTEXT_FROM_BUNDLE=1 bash "$bundle/install.sh" "${INSTALL_ARGS[@]}"
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
  TOOL_ARCH="$(normalise_arch)"
}

activate_docker_group() {
  have docker && have newgrp || return 1
  id -nG "$(id -un)" | tr ' ' '\n' | grep -qx docker || return 1
  newgrp docker -c 'docker info >/dev/null 2>&1' || return 1

  local command
  build_install_args
  printf -v command '%q ' env PLACECONTEXT_FROM_BUNDLE=1 bash "$ROOT/install.sh" "${INSTALL_ARGS[@]}"
  say "Activating Docker group access"
  exec newgrp docker -c "$command"
}

install_rootless_docker() {
  local installer actual docker_log socket
  have newuidmap && have newgidmap \
    || die "rootless Docker needs newuidmap and newgidmap configured by the host"
  { grep -q "^$(id -un):" /etc/subuid || grep -q "^$(id -u):" /etc/subuid; } \
    || die "rootless Docker needs a subordinate UID range for $(id -un) in /etc/subuid"
  { grep -q "^$(id -un):" /etc/subgid || grep -q "^$(id -u):" /etc/subgid; } \
    || die "rootless Docker needs a subordinate GID range for $(id -un) in /etc/subgid"
  if [[ -r /proc/sys/kernel/unprivileged_userns_clone ]] \
    && [[ "$(< /proc/sys/kernel/unprivileged_userns_clone)" != 1 ]]; then
    die "rootless Docker needs kernel.unprivileged_userns_clone=1"
  fi

  XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$HOME/.docker/run}"
  mkdir -p "$XDG_RUNTIME_DIR"
  chmod 0700 "$XDG_RUNTIME_DIR"
  socket="$XDG_RUNTIME_DIR/docker.sock"
  export XDG_RUNTIME_DIR DOCKER_HOST="unix://$socket" PATH="$ROOT/bin:$PATH"

  if [[ ! -x "$ROOT/bin/dockerd-rootless.sh" ]]; then
    installer="$(mktemp "${TMPDIR:-/tmp}/placecontext-docker.XXXXXX")"
    say "Installing rootless Docker"
    download "https://raw.githubusercontent.com/docker/docker-install/$ROOTLESS_DOCKER_INSTALLER_COMMIT/rootless-install.sh" "$installer"
    actual="$(sha256_file "$installer")"
    [[ "$actual" == "$ROOTLESS_DOCKER_INSTALLER_SHA256" ]] \
      || { rm -f "$installer"; die "rootless Docker installer verification failed"; }
    sed -i \
      -e "s|\$LOAD_SCRIPT_COMMIT_SHA|$ROOTLESS_DOCKER_INSTALLER_COMMIT|g" \
      -e "s|\$LOAD_SCRIPT_STABLE_LATEST|$ROOTLESS_DOCKER_VERSION|g" \
      -e "s|\$LOAD_SCRIPT_TEST_LATEST|$ROOTLESS_DOCKER_VERSION|g" \
      "$installer"
    DOCKER_BIN="$ROOT/bin" sh "$installer" \
      || { rm -f "$installer"; die "rootless Docker setup failed; check the prerequisite message above"; }
    rm -f "$installer"
  fi

  if ! docker info >/dev/null 2>&1; then
    say "Starting rootless Docker"
    docker_log="$ROOT/logs/docker.log"
    nohup "$ROOT/bin/dockerd-rootless.sh" >"$docker_log" 2>&1 &
    printf '%s\n' "$!" > "$ROOT/state/docker.pid"
    for _ in {1..30}; do
      docker info >/dev/null 2>&1 && break
      sleep 1
    done
    docker info >/dev/null 2>&1 || die "rootless Docker did not start; see $docker_log"
  fi
}

install_system_requirements() {
  have curl || die "curl is required"
  have tar || die "tar is required"

  if [[ "$TOOL_OS" == darwin ]] && ! have docker; then
    have brew || die "Homebrew is required to install Docker and Colima on macOS"
    say "Installing Docker and Colima"
    brew install docker colima
    colima start
  fi

  if ! docker info >/dev/null 2>&1; then
    if [[ "$TOOL_OS" == linux ]]; then
      activate_docker_group || install_rootless_docker
    else
      die "Docker is installed but is not running"
    fi
  fi
  have openssl || die "openssl is required"
}

install_python() {
  [[ "$INSTALL_AI" == 1 && -z "$SHARD_ENDPOINTS" ]] || return 0
  if have python3 && python3 -c 'import sys; raise SystemExit(sys.version_info < (3, 11))' 2>/dev/null; then
    return
  fi

  say "Installing Python 3.11+"
  if [[ "$TOOL_OS" == darwin ]]; then
    have brew || die "Homebrew is required to install Python on macOS"
    brew install python
  else
    die "Python 3.11+ with venv is required for local AI; install it as your user or rerun with --no-ai"
  fi
  have python3 && python3 -c 'import sys; raise SystemExit(sys.version_info < (3, 11))' \
    || die "Python 3.11+ with venv is unavailable"
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
  kubectl apply -f "$ROOT/k3s/namespaces.yaml" >/dev/null
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
  if [[ "$INSTALL_AI" == 1 ]]; then
    if [[ "$AI_TOKEN_PROVIDED" == 1 ]]; then
      kubectl -n "$NAMESPACE" create secret generic placecontext-ai \
        --from-literal="api-token=$AI_TOKEN" \
        --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    elif kubectl -n "$NAMESPACE" get secret placecontext-ai >/dev/null 2>&1; then
      AI_TOKEN="$(kubectl -n "$NAMESPACE" get secret placecontext-ai \
        -o go-template='{{index .data "api-token" | base64decode}}')"
    else
      AI_TOKEN="$(random_hex 32)"
      kubectl -n "$NAMESPACE" create secret generic placecontext-ai \
        --from-literal="api-token=$AI_TOKEN" >/dev/null
    fi
  fi

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
  [[ "$INSTALL_AI" == 1 ]] || return 0
  [[ -z "$SHARD_ENDPOINTS" ]] || return 0
  have python3 || die "Python 3.11+ is required for the local inference worker"
  have openssl || die "openssl is required to generate the AI authentication token"
  python3 -c 'import sys; raise SystemExit(sys.version_info < (3, 11))' \
    || die "Python 3.11+ is required for the local inference worker"

  [[ -n "$AI_TOKEN" ]] || AI_TOKEN="$(random_hex 32)"
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
      -e "s|__AI_TOKEN__|$AI_TOKEN|g" \
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
      -e "s|__AI_TOKEN__|$AI_TOKEN|g" \
      -e "s|__STATE_DIR__|$ROOT/ai|g" \
      -e "s|__LOG_DIR__|$ROOT/logs|g" \
      "$ROOT/local-ai/placecontext-ai.service" > "$unit_dir/placecontext-ai.service"
    systemctl --user daemon-reload
    systemctl --user enable --now placecontext-ai.service
  else
    SHARD_SPEC="$shard_spec" PLACECONTEXT_AI_TOKEN="$AI_TOKEN" \
      nohup "$venv/bin/python" "$worker" --model "$MODEL" --host 0.0.0.0 --port 8080 \
      >"$ROOT/logs/ai-worker.log" 2>&1 &
    printf '%s\n' "$!" > "$ROOT/state/ai-worker.pid"
  fi
  SHARD_ENDPOINTS="http://host.k3d.internal:8080"
  ok "AI shard $shard_spec configured (model downloads on first start)"
  if [[ "$AI_SHARD_ONLY" == 1 ]]; then
    printf '    Controller token: %s\n' "$AI_TOKEN"
    printf '    Keep this token private and configure the controller with --ai-token.\n'
  fi
}

configure_cluster_ai() {
  [[ "$INSTALL_AI" == 1 ]] || return 0
  local -a endpoints=()
  local endpoint index=0 args=()
  IFS=',' read -r -a endpoints <<< "$SHARD_ENDPOINTS"
  [[ "${#endpoints[@]}" -gt 0 && -n "${endpoints[0]}" ]] || die "no shard endpoints were configured"
  [[ -n "$AI_TOKEN" ]] || die "an AI token is required to configure shard endpoints"
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
  install_python
  if [[ "$AI_SHARD_ONLY" == 1 ]]; then
    start_worker
    ok "AI shard is listening on port 8080"
    printf '    Configure the controller with this machine\047s reachable URL.\n'
    return
  fi

  install_system_requirements
  export PATH="$ROOT/bin:$PATH"
  install_k3d
  install_kubectl

  if [[ -n "$SHARD_ENDPOINTS" && "$AI_TOKEN_PROVIDED" == 0 ]]; then
    die "--ai-token is required with --shard-endpoints (use the token printed by the shard installer)"
  fi

  say "Starting local k3s cluster"
  if k3d cluster list 2>/dev/null | awk 'NR > 1 {print $1}' | grep -qx "$CLUSTER_NAME"; then
    k3d cluster start "$CLUSTER_NAME" >/dev/null 2>&1 || true
  else
    k3d cluster create "$CLUSTER_NAME" --agents 1 \
      --api-port 127.0.0.1:0 \
      --port "$PORT:80@loadbalancer" --wait
  fi
  export KUBECONFIG
  KUBECONFIG="$(k3d kubeconfig write "$CLUSTER_NAME")"

  local api_binding api_port runtime_image cluster_endpoint
  api_binding="$(docker port "k3d-$CLUSTER_NAME-serverlb" 6443/tcp 2>/dev/null | head -n1 || true)"
  [[ "$api_binding" == 127.0.0.1:* ]] \
    || die "cluster API port is unavailable; rerun with a new PLACECONTEXT_CLUSTER name"
  api_port="${api_binding##*:}"
  [[ "$api_port" =~ ^[1-9][0-9]*$ ]] || die "cluster API port is invalid: $api_binding"
  sed -E "s#server: https://[^:]+:[0-9]+#server: https://127.0.0.1:$api_port#" \
    "$KUBECONFIG" > "$KUBECONFIG.tmp"
  mv "$KUBECONFIG.tmp" "$KUBECONFIG"

  runtime_image="$(awk -F'"' '/runtime_image:/ {print $2; exit}' "$ROOT/local-ai/config.yaml")"
  [[ "$runtime_image" == ghcr.io/* ]] || die "release does not contain a valid GHCR runtime image"

  configure_secrets
  start_worker
  say "Applying PlaceContext"
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/postgres.yaml" >/dev/null
  kubectl apply -f "$ROOT/k3s/network-policies.yaml" >/dev/null
  cluster_endpoint="http://placecontext-cluster-host:8081/api/cluster"
  [[ "$INSTALL_AI" == 1 ]] || cluster_endpoint=""
  sed -e "s|__IMAGE__|$runtime_image|g" \
      -e 's|__IMAGE_PULL_POLICY__|IfNotPresent|g' \
      -e "s|__CLUSTER_ENDPOINT__|$cluster_endpoint|g" \
      "$ROOT/k3s/placecontext.yaml" | kubectl -n "$NAMESPACE" apply -f - >/dev/null
  kubectl apply -f "$ROOT/k3s/local-ingress.yaml" >/dev/null
  configure_cluster_ai

  if [[ "$WAIT" == 1 ]]; then
    say "Waiting for PlaceContext"
    kubectl -n "$NAMESPACE" rollout status deployment/placecontext --timeout=5m
  fi
  ok "PlaceContext is available at http://localhost:$PORT"
  printf '    Installed files: %s\n' "$ROOT"
}

deploy_local
