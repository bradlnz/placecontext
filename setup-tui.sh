#!/usr/bin/env bash
#
# setup-tui.sh — simple interactive setup for PlaceContext.
#
# Recommended path:
#   1) install local prerequisites (./setup.sh)
#   2) install and start a local cluster (placecontext install --docker)
#
# What it does when installing a cluster:
#   - uses a local image tarball if available
#   - otherwise pulls placecontext image from remote registry
#   - set LOCAL_IMAGE_ONLY=1 to force offline/local package mode only
#
# Advanced paths:
#   - local-only mode (./run.sh)
#   - service install (placecontext install --service)
#   - status/doctor checks
#
# Usage:
#   ./setup-tui.sh

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
SETUP_SCRIPT="$ROOT_DIR/setup.sh"
RUN_SCRIPT="$ROOT_DIR/run.sh"
CLUSTER_CLI="$ROOT_DIR/deploy/placecontext"
LOCAL_IMAGE_TAR="${ROOT_DIR}/deploy/placecontext-local.tar"
DEFAULT_IMAGE="${PCTL_IMAGE:-ghcr.io/bradlnz/placecontext:local}"
PCTL_CLUSTER="${PCTL_CLUSTER:-placecontext}"
LOCAL_IMAGE_ONLY="${LOCAL_IMAGE_ONLY:-0}"

if [ ! -f "$LOCAL_IMAGE_TAR" ] && [ -f "$ROOT_DIR/lib/placecontext-local.tar" ]; then
  LOCAL_IMAGE_TAR="${ROOT_DIR}/lib/placecontext-local.tar"
fi

if [ "${1-}" = "-h" ] || [ "${1-}" = "--help" ]; then
  sed -n '1,120p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
fi

if [ ! -t 0 ] || [ ! -t 1 ]; then
  echo "setup-tui needs an interactive terminal."
  exit 1
fi

if ! [ -x "$SETUP_SCRIPT" ] || ! [ -x "$RUN_SCRIPT" ] || ! [ -x "$CLUSTER_CLI" ]; then
  echo "setup-tui expects setup.sh, run.sh, and deploy/placecontext in this repo." >&2
  exit 1
fi

C_CYAN=$'\033[1;36m'
C_GREEN=$'\033[1;32m'
C_RED=$'\033[1;31m'
C_YELLOW=$'\033[1;33m'
C_DIM=$'\033[2m'
C_RESET=$'\033[0m'

say() { printf '%s==>%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
ok()  { printf '%s✓%s %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn(){ printf '%s!%s %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
err() { printf '%s✗%s %s\n' "$C_RED" "$C_RESET" "$*" >&2; }

run_cmd() {
  local label="$1"
  shift

  printf '\n%s\n' "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  say "$label"
  set +e
  (cd "$ROOT_DIR" && "$@")
  local rc=$?
  set -e

  if [ "$rc" = 0 ]; then
    ok "${label} finished."
  else
    err "${label} failed (exit ${rc})."
  fi
  return "$rc"
}

start_local() {
  local fresh="$1"
  if [ "$fresh" = fresh ]; then
    run_cmd "Starting local app (fresh DB)" "$RUN_SCRIPT" --fresh
  else
    run_cmd "Starting local app" "$RUN_SCRIPT"
  fi
}

install_cluster_docker() {
  local pull_policy="Always"
  local image_source="${PCTL_IMAGE_TAR:=$LOCAL_IMAGE_TAR}"
  local required_image="${LOCAL_IMAGE_ONLY}"

  if [ -s "$image_source" ]; then
    pull_policy="IfNotPresent"
  elif [ "$LOCAL_IMAGE_ONLY" -eq 1 ]; then
    err "Missing local image tarball at $image_source"
    warn "Set LOCAL_IMAGE_ONLY=0 to allow remote image pulls."
    return 1
  else
    say "No local image tarball found — pulling from remote image registry."
  fi

  if [ -n "$image_source" ]; then
    if [ -s "$image_source" ]; then
      say "Using local image tarball: $image_source"
    fi
    local required_image="${LOCAL_IMAGE_ONLY}"
    run_cmd "Installing local cluster (k3d Docker mode)" env \
      "PCTL_IMAGE_TAR=$image_source" \
      "PCTL_IMAGE=$DEFAULT_IMAGE" \
      "PCTL_IMAGE_PULL_POLICY=$pull_policy" \
      "PCTL_LOCAL_IMAGE_REQUIRED=$required_image" \
      "$CLUSTER_CLI" install --docker
  fi
}

install_cluster_service() {
  run_cmd "Installing system service cluster (k3s) — requires sudo" "$CLUSTER_CLI" install --service
}

run_local_setup() {
  if run_cmd "Preparing local prerequisites" "$SETUP_SCRIPT"; then
    install_cluster_docker
  fi
}

check_status() {
  run_cmd "Checking environment" "$CLUSTER_CLI" doctor
  if command -v k3d >/dev/null 2>&1; then
    if k3d cluster list 2>/dev/null | grep -q -E "^${PCTL_CLUSTER}([[:space:]]|$)"; then
      ok "k3d cluster '${PCTL_CLUSTER}' is present."
    else
      warn "No k3d cluster '${PCTL_CLUSTER}' found."
    fi
  fi

  if command -v curl >/dev/null 2>&1; then
    if curl -fsS http://localhost:7700/ >/dev/null 2>&1; then
      ok "Portal reachable at http://localhost:7700"
    else
      warn "Portal not reachable at http://localhost:7700 yet."
    fi
  fi

  run_cmd "Checking cluster apps" "$CLUSTER_CLI" status
}

ask_enter() {
  read -r -p "Press Enter to continue..."
}

draw_menu() {
  clear
  printf '%sPlaceContext setup wizard%s\n' "$C_CYAN" "$C_RESET"
  printf 'This is for local testing/dev, not production k8s operations.\n'
  printf 'If in doubt, choose option 1.\n'
  printf 'If the image tarball is missing, it will be downloaded automatically.\n\n'
  printf '1) Recommended: full local setup (setup.sh + cluster install)\n'
  printf '2) Local app only (no cluster): setup then run locally\n'
  printf '3) Cluster install only: k3d/docker (same as install --docker)\n'
  printf '4) Advanced: system service install (--service, needs sudo)\n'
  printf '5) Check setup and cluster status\n'
  printf '6) Exit\n\n'
}

while true; do
  draw_menu
  read -r -p "Choose [1-6]: " choice
  case "$choice" in
    1)
      run_local_setup && check_status
      ask_enter
      ;;
    2)
      run_cmd "Running local prerequisites (no cluster)" "$SETUP_SCRIPT" || true
      start_local normal
      ask_enter
      ;;
    3)
      install_cluster_docker
      check_status
      ask_enter
      ;;
    4)
      install_cluster_service
      check_status
      ask_enter
      ;;
    5)
      check_status
      ask_enter
      ;;
    6|q|Q)
      echo "Bye."
      exit 0
      ;;
    *)
      warn "Invalid option."
      sleep 1
      ;;
  esac
done
