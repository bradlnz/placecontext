#!/usr/bin/env bash
# PlaceContext local release installer.
#
#   curl -fsSL https://get.placecontext.io/install.sh | bash
#
# The GitHub release bundle contains deployment files only; the runtime is pulled from GHCR.
# The installer provisions its tools, creates k3d, and applies the manifests.
set -euo pipefail

# Non-interactive SSH sessions on minimal Linux images often omit sbin directories.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"

VERSION="${PLACECONTEXT_VERSION:-latest}"
BASE_URL="${PLACECONTEXT_BASE_URL:-https://github.com/bradlnz/placecontext/releases}"
INSTALL_DIR="${PLACECONTEXT_HOME:-$HOME/.local/share/placecontext}"
PORT="${PLACECONTEXT_PORT:-7700}"
CLUSTER_NAME="${PLACECONTEXT_CLUSTER:-placecontext-local}"
NAMESPACE="placecontext"
WAIT=1
FROM_BUNDLE="${PLACECONTEXT_FROM_BUNDLE:-0}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --no-ai) shift ;; # Retain existing installs that explicitly opted out.
    --no-wait) WAIT=0; shift ;;
    --from-bundle) FROM_BUNDLE=1; shift ;;
    -h|--help)
      printf '%s\n' \
        'PlaceContext local release installer' \
        'Linux may use sudo to install and start Docker for the local k3s cluster.' \
        '' \
        'Usage:' \
        '  install.sh [--version TAG] [--port PORT]' \
        '' \
        'Options:' \
        '  --version TAG          GitHub release version (default: latest)' \
        '  --install-dir DIR      Install location' \
        '  --port PORT            Local portal port (default: 7700)' \
        '  --no-wait              Return before the Host rollout completes'
      exit 0
      ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done

say() { printf '==> %s\n' "$*"; }
ok() { printf '  ✓ %s\n' "$*"; }
die() { printf '  ✗ %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

as_root() {
  if [[ "$(id -u)" == 0 ]]; then
    "$@"
  elif have sudo && sudo -n true 2>/dev/null; then
    sudo -n "$@"
  else
    die "passwordless sudo is required to provision Docker on Linux"
  fi
}

INSTALL_ARGS=()
build_install_args() {
  INSTALL_ARGS=(
    --version "$VERSION"
    --install-dir "$INSTALL_DIR"
    --port "$PORT"
  )
  [[ "$WAIT" == 1 ]] || INSTALL_ARGS+=(--no-wait)
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
[[ -d "$SCRIPT_DIR/k3s" && -f "$SCRIPT_DIR/RUNTIME_IMAGE" ]] \
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
  DOCKER_HOST=unix:///var/run/docker.sock newgrp docker -c 'docker info >/dev/null 2>&1' || return 1

  local command
  build_install_args
  printf -v command '%q ' env \
    PLACECONTEXT_FROM_BUNDLE=1 \
    DOCKER_HOST=unix:///var/run/docker.sock \
    bash "$ROOT/install.sh" "${INSTALL_ARGS[@]}"
  say "Activating Docker group access"
  exec newgrp docker -c "$command"
}

install_linux_docker() {
  if DOCKER_HOST=unix:///var/run/docker.sock docker info >/dev/null 2>&1; then
    export DOCKER_HOST=unix:///var/run/docker.sock
    return
  fi

  say "Installing Docker"
  if have apt-get; then
    as_root apt-get update
    as_root env DEBIAN_FRONTEND=noninteractive apt-get install -y docker.io
  elif have dnf; then
    as_root dnf install -y docker
  elif have pacman; then
    as_root pacman -Sy --noconfirm docker
  else
    die "install Docker, then rerun (supported package managers: apt, dnf, pacman)"
  fi
  have systemctl || die "systemd is required to start Docker on Linux"
  as_root systemctl enable --now docker
  as_root usermod -aG docker "$(id -un)"
  activate_docker_group \
    || die "Docker was installed but group access could not be activated; sign out and rerun"
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

  if [[ "$TOOL_OS" == linux ]]; then
    install_linux_docker
  elif ! docker info >/dev/null 2>&1; then
    die "Docker is installed but is not running"
  fi
  have openssl || die "openssl is required"
}

install_k3d() {
  have k3d && return
  local release="${K3D_VERSION:-v5.8.3}" name sums expected actual
  name="k3d-$TOOL_OS-$TOOL_ARCH"
  sums="$(mktemp "${TMPDIR:-/tmp}/k3d-checksums.XXXXXX")"
  say "Installing k3d $release"
  download "https://github.com/k3d-io/k3d/releases/download/$release/$name" "$ROOT/bin/k3d"
  download "https://github.com/k3d-io/k3d/releases/download/$release/checksums.txt" "$sums"
  expected="$(awk -v name="$name" '{ path=$2; sub(/^.*\//, "", path); if (path == name) { print $1; exit } }' "$sums")"
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

deploy_local() {
  detect_platform
  install_system_requirements
  export PATH="$ROOT/bin:$PATH"
  install_k3d
  install_kubectl

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

  local api_binding api_port runtime_image
  api_binding="$(docker port "k3d-$CLUSTER_NAME-serverlb" 6443/tcp 2>/dev/null | head -n1 || true)"
  [[ "$api_binding" == 127.0.0.1:* ]] \
    || die "cluster API port is unavailable; rerun with a new PLACECONTEXT_CLUSTER name"
  api_port="${api_binding##*:}"
  [[ "$api_port" =~ ^[1-9][0-9]*$ ]] || die "cluster API port is invalid: $api_binding"
  sed -E "s#server: https://[^:]+:[0-9]+#server: https://127.0.0.1:$api_port#" \
    "$KUBECONFIG" > "$KUBECONFIG.tmp"
  mv "$KUBECONFIG.tmp" "$KUBECONFIG"

  runtime_image="$(cat "$ROOT/RUNTIME_IMAGE")"
  [[ "$runtime_image" == ghcr.io/* ]] || die "release does not contain a valid GHCR runtime image"

  configure_secrets
  say "Applying PlaceContext"
  kubectl -n "$NAMESPACE" apply -f "$ROOT/k3s/postgres.yaml" >/dev/null
  say "Waiting for PostgreSQL"
  kubectl -n "$NAMESPACE" rollout status deployment/placecontext-db --timeout=10m
  kubectl apply -f "$ROOT/k3s/network-policies.yaml" >/dev/null
  kubectl apply -f "$ROOT/k3s/hosted-services.yaml" >/dev/null
  sed -e "s|__IMAGE__|$runtime_image|g" \
      -e 's|__IMAGE_PULL_POLICY__|IfNotPresent|g' \
      "$ROOT/k3s/placecontext.yaml" | kubectl apply -f - >/dev/null
  kubectl apply -f "$ROOT/k3s/local-ingress.yaml" >/dev/null

  if [[ "$WAIT" == 1 ]]; then
    say "Waiting for PlaceContext"
    kubectl -n "$NAMESPACE" rollout status deployment/placecontext --timeout=5m
  fi
  ok "PlaceContext is available at http://localhost:$PORT"
  printf '    Installed files: %s\n' "$ROOT"
}

deploy_local
