#!/usr/bin/env bash
#
# integration_test_install.sh — post-release install + cluster smoke test
#
# Downloads the published install script into an isolated Ubuntu container,
# installs the placecontext CLI, (optionally) brings up a k3d cluster via
# `placecontext install --docker`, and prints a PASS/FAIL report.
#
# Intended as a post-upload check after `./tools/release.sh --upload`.
#
# Usage:
#   ./tools/integration_test_install.sh
#   ./tools/integration_test_install.sh --base https://placecontext.syd1.digitaloceanspaces.com
#   ./tools/integration_test_install.sh --cli-only          # skip k3d cluster
#   ./tools/integration_test_install.sh --keep              # leave container + cluster
#   ./tools/integration_test_install.sh --port 7711
#   ./tools/integration_test_install.sh --cluster pc-itest
#
# Environment (overrides):
#   PLACECONTEXT_BASE_URL / --base     install download base (default: Spaces origin)
#   IT_UBUNTU_IMAGE                    container image (default: ubuntu:24.04)
#   IT_CLUSTER / --cluster             k3d cluster name (default: pc-itest-<pid>)
#   IT_PORT / --port                   host portal port (default: free high port)
#   IT_AGENTS                          k3d agents (default: 1)
#   IT_KEEP=1 / --keep                 skip cleanup
#   IT_CLI_ONLY=1 / --cli-only         only verify curl | install.sh + CLI
#   IT_ADMIN_EMAIL / PASSWORD / NAME   non-interactive admin setup
#   IT_TIMEOUT_SECS                    max wall clock (default: 900)
#
# Exit codes:
#   0  all enabled checks passed
#   1  one or more checks failed
#   2  host preflight failed (docker missing, etc.)
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# ── defaults ─────────────────────────────────────────────────────────────────────────────────────
BASE_URL="${PLACECONTEXT_BASE_URL:-${PLACECONTEXT_BUCKET_URL:-https://placecontext.syd1.digitaloceanspaces.com}}"
UBUNTU_IMAGE="${IT_UBUNTU_IMAGE:-ubuntu:24.04}"
CLUSTER="${IT_CLUSTER:-pc-itest-$$}"
PORT="${IT_PORT:-}"
# 0 agents = single-node server only (lighter on disk/RAM for CI / small hosts)
AGENTS="${IT_AGENTS:-0}"
KEEP="${IT_KEEP:-0}"
CLI_ONLY="${IT_CLI_ONLY:-0}"
TIMEOUT_SECS="${IT_TIMEOUT_SECS:-900}"
CONTAINER="pc-itest-$$"
ADMIN_EMAIL="${IT_ADMIN_EMAIL:-admin@example.com}"
ADMIN_NAME="${IT_ADMIN_NAME:-Integration Admin}"
ADMIN_PASSWORD="${IT_ADMIN_PASSWORD:-IntegrationTestPass123!}"

while [ $# -gt 0 ]; do
  case "$1" in
    --base)      BASE_URL="$2"; shift 2;;
    --cluster)   CLUSTER="$2"; shift 2;;
    --port)      PORT="$2"; shift 2;;
    --agents)    AGENTS="$2"; shift 2;;
    --image)     UBUNTU_IMAGE="$2"; shift 2;;
    --keep)      KEEP=1; shift;;
    --cli-only)  CLI_ONLY=1; shift;;
    --timeout)   TIMEOUT_SECS="$2"; shift 2;;
    -h|--help)
      sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

BASE_URL="${BASE_URL%/}"

if [ -t 1 ]; then
  C_CYAN=$'\033[1;36m'; C_GREEN=$'\033[1;32m'; C_YELLOW=$'\033[1;33m'
  C_BOLD=$'\033[1m'; C_RESET=$'\033[0m'; C_RED=$'\033[1;31m'; C_DIM=$'\033[2m'
else
  C_CYAN=""; C_GREEN=""; C_YELLOW=""; C_BOLD=""; C_RESET=""; C_RED=""; C_DIM=""
fi
say()  { printf '%s==>%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
ok()   { printf '%s✓%s %s\n'  "$C_GREEN" "$C_RESET" "$*"; }
warn() { printf '%s!%s %s\n'  "$C_YELLOW" "$C_RESET" "$*" >&2; }
die()  { printf '%s✗%s %s\n'  "$C_RED" "$C_RESET" "$*" >&2; exit 2; }
have() { command -v "$1" >/dev/null 2>&1; }

# ── result table ─────────────────────────────────────────────────────────────────────────────────
# RESULT_LINES accumulates "name|status|detail"
RESULT_LINES=()
PASS_N=0
FAIL_N=0
SKIP_N=0

record() {
  # record <name> <pass|fail|skip> <detail>
  local name="$1" st="$2" detail="${3:-}"
  RESULT_LINES+=("${name}|${st}|${detail}")
  case "$st" in
    pass) PASS_N=$((PASS_N + 1)); ok "$name${detail:+ — $detail}";;
    fail) FAIL_N=$((FAIL_N + 1)); printf '%s✗%s %s%s\n' "$C_RED" "$C_RESET" "$name" "${detail:+ — $detail}";;
    skip) SKIP_N=$((SKIP_N + 1)); printf '%s·%s %s%s\n' "$C_DIM" "$C_RESET" "$name" "${detail:+ — $detail}";;
  esac
}

check() {
  # check <name> <command…>  — records pass/fail from exit status
  local name="$1"; shift
  local out ec=0
  out="$("$@" 2>&1)" && ec=0 || ec=$?
  if [ "$ec" -eq 0 ]; then
    record "$name" pass "$(printf '%s' "$out" | tr '\n' ' ' | head -c 160)"
  else
    record "$name" fail "$(printf '%s' "$out" | tr '\n' ' ' | head -c 200)"
  fi
  return 0
}

print_report() {
  printf '\n%s════════ Post-release install check ════════%s\n' "$C_BOLD" "$C_RESET"
  printf '  base:     %s\n' "$BASE_URL"
  printf '  image:    %s\n' "$UBUNTU_IMAGE"
  printf '  cluster:  %s\n' "$CLUSTER"
  printf '  port:     %s\n' "${PORT:-n/a}"
  printf '  cli-only: %s\n' "$CLI_ONLY"
  printf '\n  %-32s  %-6s  %s\n' "CHECK" "STATUS" "DETAIL"
  printf '  %-32s  %-6s  %s\n' "-----" "------" "------"
  local line name st detail
  for line in "${RESULT_LINES[@]+"${RESULT_LINES[@]}"}"; do
    name="${line%%|*}"; rest="${line#*|}"
    st="${rest%%|*}"; detail="${rest#*|}"
    printf '  %-32s  %-6s  %s\n' "$name" "$st" "$detail"
  done
  printf '\n  totals: %s%d pass%s · %s%d fail%s · %d skip\n' \
    "$C_GREEN" "$PASS_N" "$C_RESET" \
    "$C_RED" "$FAIL_N" "$C_RESET" \
    "$SKIP_N"
  if [ "$FAIL_N" -eq 0 ]; then
    printf '\n%sRESULT: PASS%s\n\n' "$C_GREEN" "$C_RESET"
  else
    printf '\n%sRESULT: FAIL%s\n\n' "$C_RED" "$C_RESET"
  fi
}

# ── cleanup ──────────────────────────────────────────────────────────────────────────────────────
CLEANED=0
cleanup() {
  [ "$CLEANED" = 1 ] && return 0
  CLEANED=1
  if [ "$KEEP" = 1 ]; then
    warn "keeping container=$CONTAINER cluster=$CLUSTER (IT_KEEP/ --keep)"
    return 0
  fi
  say "Cleaning up…"
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  if have k3d; then
    k3d cluster delete "$CLUSTER" >/dev/null 2>&1 || true
  fi
  # Also try delete from inside host if container already gone
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# Wall-clock guard
START_TS="$(date +%s)"
guard_timeout() {
  local now; now="$(date +%s)"
  if [ $((now - START_TS)) -ge "$TIMEOUT_SECS" ]; then
    record "timeout" fail "exceeded ${TIMEOUT_SECS}s"
    print_report
    exit 1
  fi
}

# ── free port ────────────────────────────────────────────────────────────────────────────────────
pick_port() {
  if [ -n "$PORT" ]; then
    printf '%s' "$PORT"
    return 0
  fi
  # Prefer 7711..7799 then ephemeral
  local p
  for p in $(seq 7711 7799); do
    if ! ss -ltn 2>/dev/null | grep -qE ":${p}\\s"; then
      printf '%s' "$p"
      return 0
    fi
  done
  python3 -c 'import socket; s=socket.socket(); s.bind(("",0)); print(s.getsockname()[1]); s.close()' 2>/dev/null \
    || printf '17711'
}

# ── host preflight ───────────────────────────────────────────────────────────────────────────────
say "Host preflight"
have docker || die "docker is required on the host"
docker info >/dev/null 2>&1 || die "docker daemon not reachable"
record "host.docker" pass "$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo ok)"

if [ "$CLI_ONLY" != 1 ]; then
  # k3d on host is only used for cleanup; cluster is created from inside the container
  # via the mounted docker socket.
  if ! docker info >/dev/null 2>&1; then
    die "docker daemon required for cluster install"
  fi
  avail_kb="$(df -Pk / | awk 'NR==2{print $4}')"
  if [ "${avail_kb:-0}" -lt 4000000 ]; then
    warn "low disk: ~$((avail_kb / 1024)) MiB free — k3d nodes may taint with disk-pressure"
    record "host.disk" fail "${avail_kb} KiB free (want ≥ ~4 GiB)"
  else
    record "host.disk" pass "$((avail_kb / 1024)) MiB free"
  fi
else
  record "host.disk" skip "cli-only"
fi

PORT="$(pick_port)"
say "Using portal port ${PORT}, cluster ${CLUSTER}, container ${CONTAINER}"

# ── start isolated Ubuntu ────────────────────────────────────────────────────────────────────────
say "Starting ${UBUNTU_IMAGE} container"
docker pull -q "$UBUNTU_IMAGE" >/dev/null 2>&1 || docker pull "$UBUNTU_IMAGE" >/dev/null
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true

DOCKER_RUN_ARGS=(
  -d --name "$CONTAINER"
  --network host
  -e DEBIAN_FRONTEND=noninteractive
)
if [ "$CLI_ONLY" != 1 ]; then
  DOCKER_RUN_ARGS+=(-v /var/run/docker.sock:/var/run/docker.sock)
fi

docker run "${DOCKER_RUN_ARGS[@]}" "$UBUNTU_IMAGE" sleep infinity >/dev/null
record "container.start" pass "$CONTAINER"

dexec() {
  docker exec -e DEBIAN_FRONTEND=noninteractive "$CONTAINER" bash -lc "$*"
}

# ── install prereqs inside container ─────────────────────────────────────────────────────────────
say "Installing container prereqs (curl, ca-certificates, tar, openssl…)"
dexec '
set -euo pipefail
apt-get update -qq
apt-get install -y -qq curl ca-certificates tar gzip openssl python3 gnupg >/dev/null
' || { record "container.prereqs" fail "apt install failed"; print_report; exit 1; }
record "container.prereqs" pass "curl tar openssl python3"

if [ "$CLI_ONLY" != 1 ]; then
  say "Installing docker CLI + k3d + kubectl inside container"
  dexec '
set -euo pipefail
# Docker CLI against host daemon
install -m 0755 -d /etc/apt/keyrings
if [ ! -f /etc/apt/keyrings/docker.gpg ]; then
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg
fi
. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" > /etc/apt/sources.list.d/docker.list
apt-get update -qq
apt-get install -y -qq docker-ce-cli >/dev/null
docker info >/dev/null

# kubectl
KVER=$(curl -fsSL https://dl.k8s.io/release/stable.txt)
curl -fsSLo /usr/local/bin/kubectl "https://dl.k8s.io/release/${KVER}/bin/linux/$(dpkg --print-architecture)/kubectl"
chmod +x /usr/local/bin/kubectl

# k3d
curl -fsSL https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash
k3d version >/dev/null
kubectl version --client >/dev/null
' || { record "container.k3d_stack" fail "docker/k3d/kubectl install failed"; print_report; exit 1; }
  record "container.k3d_stack" pass "docker-cli + k3d + kubectl"
else
  record "container.k3d_stack" skip "cli-only"
fi

# ── published install.sh ─────────────────────────────────────────────────────────────────────────
say "Downloading + running published install.sh from ${BASE_URL}"
guard_timeout

INSTALL_OUT="$(dexec "
set -euo pipefail
export PATH=\"/root/.local/bin:\$PATH\"
curl -fsSL '${BASE_URL}/install.sh' | bash -s -- --no-launch --base '${BASE_URL}'
export PATH=\"/root/.local/bin:\$PATH\"
placecontext version
cat /root/.placecontext/VERSION
test -x /root/.placecontext/bin/placecontext
test -L /root/.local/bin/placecontext
test -d /root/.placecontext/lib
test -s /root/.placecontext/lib/placecontext-local.tar || test -f /root/.placecontext/lib/placecontext-engine
echo INSTALL_CLI_OK
" 2>&1)" && install_ec=0 || install_ec=$?

if [ "$install_ec" -eq 0 ] && printf '%s' "$INSTALL_OUT" | grep -q INSTALL_CLI_OK; then
  VER="$(printf '%s' "$INSTALL_OUT" | grep -E '^[0-9a-f]{7,40}$|^[0-9]+\.[0-9]+' | tail -1 || true)"
  [ -z "$VER" ] && VER="$(printf '%s' "$INSTALL_OUT" | tr '\n' ' ' | head -c 80)"
  record "install.cli" pass "version ${VER}"
else
  record "install.cli" fail "$(printf '%s' "$INSTALL_OUT" | tr '\n' ' ' | tail -c 240)"
  print_report
  exit 1
fi

# Smoke CLI subcommands
check "cli.version" dexec 'export PATH=/root/.local/bin:$PATH; placecontext version'
check "cli.help"    dexec 'export PATH=/root/.local/bin:$PATH; placecontext help | head -1'
check "cli.doctor"  dexec 'export PATH=/root/.local/bin:$PATH; placecontext doctor'

if [ "$CLI_ONLY" = 1 ]; then
  record "install.cluster" skip "cli-only"
  record "cluster.pods" skip "cli-only"
  record "portal.health" skip "cli-only"
  record "portal.admin" skip "cli-only"
  print_report
  [ "$FAIL_N" -eq 0 ] && exit 0 || exit 1
fi

# ── cluster install ──────────────────────────────────────────────────────────────────────────────
say "Running placecontext install --docker (cluster=${CLUSTER} port=${PORT})"
guard_timeout

# Pre-delete any leftover cluster with same name
dexec "k3d cluster delete '${CLUSTER}' >/dev/null 2>&1 || true" || true

CLUSTER_OUT="$(dexec "
set -euo pipefail
export PATH=\"/root/.local/bin:/usr/local/bin:\$PATH\"
export PLACECONTEXT_HOME=/root/.placecontext
export PCTL_CLUSTER='${CLUSTER}'
export PCTL_PORT='${PORT}'
export PCTL_AGENTS='${AGENTS}'
export PLACECONTEXT_PORTAL_URL='http://127.0.0.1:${PORT}'
export PLACECONTEXT_ADMIN_EMAIL='${ADMIN_EMAIL}'
export PLACECONTEXT_ADMIN_NAME='${ADMIN_NAME}'
export PLACECONTEXT_ADMIN_PASSWORD='${ADMIN_PASSWORD}'
placecontext install --docker
echo INSTALL_CLUSTER_OK
" 2>&1)" && cluster_ec=0 || cluster_ec=$?

if [ "$cluster_ec" -eq 0 ] && printf '%s' "$CLUSTER_OUT" | grep -q INSTALL_CLUSTER_OK; then
  record "install.cluster" pass "k3d ${CLUSTER}"
else
  # Still record partial progress for diagnostics
  record "install.cluster" fail "$(printf '%s' "$CLUSTER_OUT" | tr '\n' ' ' | tail -c 280)"
fi

# ── post-install cluster checks ──────────────────────────────────────────────────────────────────
say "Verifying cluster + portal"
guard_timeout

PODS_OUT="$(dexec "
set -euo pipefail
export PATH=/usr/local/bin:\$PATH
export KUBECONFIG=\$(k3d kubeconfig write '${CLUSTER}')
# Wait up to ~4 min for workloads
for i in \$(seq 1 48); do
  not_ready=\$(kubectl --context k3d-${CLUSTER} -n placecontext get pods --no-headers 2>/dev/null \
    | awk '\$2 !~ /^([0-9]+)\\/\\1\$/ || \$3 != \"Running\" {c++} END{print c+0}')
  total=\$(kubectl --context k3d-${CLUSTER} -n placecontext get pods --no-headers 2>/dev/null | wc -l)
  if [ \"\${total:-0}\" -gt 0 ] && [ \"\${not_ready:-1}\" -eq 0 ]; then
    kubectl --context k3d-${CLUSTER} -n placecontext get pods --no-headers
    exit 0
  fi
  sleep 5
done
echo 'pods not ready:'
kubectl --context k3d-${CLUSTER} -n placecontext get pods -o wide 2>&1 || true
kubectl --context k3d-${CLUSTER} get nodes 2>&1 || true
exit 1
" 2>&1)" && pods_ec=0 || pods_ec=$?

if [ "$pods_ec" -eq 0 ]; then
  record "cluster.pods" pass "$(printf '%s' "$PODS_OUT" | tr '\n' ';' | head -c 160)"
else
  record "cluster.pods" fail "$(printf '%s' "$PODS_OUT" | tr '\n' ' ' | tail -c 240)"
fi

# Portal health — try healthz then setup/status then root
PORTAL_OUT="$(dexec "
set -euo pipefail
base='http://127.0.0.1:${PORT}'
for i in \$(seq 1 36); do
  if curl -fsS --max-time 3 \"\${base}/healthz\" >/dev/null 2>&1; then
    echo healthz_ok
    curl -fsS --max-time 3 \"\${base}/healthz\" || true
    exit 0
  fi
  if curl -fsS --max-time 3 \"\${base}/auth/setup/status\" >/dev/null 2>&1; then
    echo setup_status_ok
    curl -fsS --max-time 3 \"\${base}/auth/setup/status\" || true
    exit 0
  fi
  code=\$(curl -sS -o /dev/null -w '%{http_code}' --max-time 3 \"\${base}/\" 2>/dev/null || echo 000)
  if [ \"\$code\" != 000 ] && [ \"\$code\" != 000000 ]; then
    echo root_http_\$code
    exit 0
  fi
  sleep 5
done
echo unreachable
exit 1
" 2>&1)" && portal_ec=0 || portal_ec=$?

if [ "$portal_ec" -eq 0 ]; then
  record "portal.health" pass "$(printf '%s' "$PORTAL_OUT" | tr '\n' ' ' | head -c 120)"
else
  record "portal.health" fail "$(printf '%s' "$PORTAL_OUT" | tr '\n' ' ' | tail -c 160)"
fi

# Admin setup status (needsAdmin should be false if credentials worked)
ADMIN_OUT="$(dexec "
set -euo pipefail
base='http://127.0.0.1:${PORT}'
body=\$(curl -fsS --max-time 5 \"\${base}/auth/setup/status\" 2>/dev/null || true)
echo \"\$body\"
if printf '%s' \"\$body\" | grep -q '\"needsAdmin\"[[:space:]]*:[[:space:]]*false'; then
  exit 0
fi
# Accept true only if portal is up but admin skipped — still a soft signal
if printf '%s' \"\$body\" | grep -q needsAdmin; then
  exit 1
fi
exit 1
" 2>&1)" && admin_ec=0 || admin_ec=$?

if [ "$admin_ec" -eq 0 ]; then
  record "portal.admin" pass "needsAdmin=false"
else
  record "portal.admin" fail "$(printf '%s' "$ADMIN_OUT" | tr '\n' ' ' | head -c 160)"
fi

# placecontext status from inside container
check "cli.status" dexec "
export PATH=/root/.local/bin:/usr/local/bin:\$PATH
export PLACECONTEXT_HOME=/root/.placecontext
export PCTL_CLUSTER='${CLUSTER}'
export PCTL_PORT='${PORT}'
placecontext status
"

print_report
[ "$FAIL_N" -eq 0 ] && exit 0 || exit 1
