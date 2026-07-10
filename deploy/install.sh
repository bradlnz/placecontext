#!/usr/bin/env bash
#
# install.sh — one command to install and launch PlaceContext.
#
# PlaceContext runs on Kubernetes under the hood — a real k3s cluster, either installed
# as a system service on this machine or inside Docker (k3d). This script downloads
# and installs everything that's needed (kubectl, k3d, even Docker itself), installs
# the global `placecontext` command (and `pctl`), brings the cluster up, and configures
# it to start on boot. After that, just type `placecontext` anywhere.
#
# Usage:
#   ./deploy/install.sh                # interactive: asks service vs docker
#   ./deploy/install.sh --docker       # k3s-in-Docker (k3d) — laptops, dev machines
#   sudo ./deploy/install.sh --service # k3s as a systemd service — servers, fleet masters
#   --no-launch                        # set everything up but don't open the TUI
#
#   (--prod is accepted as an alias for --service for older docs/scripts.)
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="${PCTL_INSTALL_BIN:-$HOME/.local/bin}"
ARCH="$(uname -m)"; case "$ARCH" in x86_64) ARCH=amd64;; aarch64|arm64) ARCH=arm64;; esac
K3D_VERSION="${K3D_VERSION:-v5.7.4}"

MODE=""; LAUNCH=1
while [ $# -gt 0 ]; do
  case "$1" in
    --service|--prod) MODE="service"; shift;;
    --docker|--dev)   MODE="docker"; shift;;
    --no-launch)      LAUNCH=0; shift;;
    -h|--help)        grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 2;;
  esac
done

C_CYAN=$'\033[1;36m'; C_GREEN=$'\033[1;32m'; C_YELLOW=$'\033[1;33m'; C_BOLD=$'\033[1m'; C_RESET=$'\033[0m'
say()  { printf '\n%s==>%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
ok()   { printf '%s✓%s %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn() { printf '%s!%s %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
die()  { printf '\033[1;31m✗\033[0m %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }
as_root() {
  if [ "$(id -u)" = 0 ]; then "$@"
  elif have sudo; then sudo "$@"
  else die "root is needed for: $* — install sudo or run as root"; fi
}

# ── 0. Choose how to run ─────────────────────────────────────────────────────────────────────────
if [ -z "$MODE" ]; then
  if [ -t 0 ]; then
    printf '\n%sPlaceContext%s — how do you want to run it?  (k3s Kubernetes under the hood either way)\n\n' "$C_BOLD" "$C_RESET"
    printf '  1) %sIn Docker%s      — k3s-in-Docker (k3d). Best for laptops and dev machines;\n'   "$C_BOLD" "$C_RESET"
    printf '                     easy to tear down, nothing installed system-wide.\n'
    printf '  2) %sAs a service%s   — k3s installed as a systemd service. Best for servers and\n' "$C_BOLD" "$C_RESET"
    printf '                     fleet masters; survives reboots on its own. Needs sudo.\n\n'
    while :; do
      printf '%schoice [1-2]: %s' "$C_BOLD" "$C_RESET"; read -r pick
      case "$pick" in 1) MODE="docker"; break;; 2) MODE="service"; break;; *) warn "pick 1 or 2";; esac
    done
  else
    MODE="docker" # non-interactive default: the containerized install
  fi
fi
[ "$MODE" = "service" ] && [ "$(id -u)" != 0 ] && ! have sudo && die "--service installs k3s system-wide — run with sudo."

mkdir -p "$BIN"
export PATH="$BIN:$PATH"

# ── 1. Dependencies (everything is downloaded for you) ──────────────────────────────────────────
say "Checking dependencies…"
have curl || die "curl is required to download the rest — install curl and re-run."

if ! have kubectl; then
  say "Installing kubectl → $BIN…"
  ver="$(curl -fsSL https://dl.k8s.io/release/stable.txt)"
  curl -fsSL -o "$BIN/kubectl" "https://dl.k8s.io/release/${ver}/bin/linux/${ARCH}/kubectl"
  chmod +x "$BIN/kubectl"; ok "kubectl ${ver}"
else ok "kubectl present"; fi

if [ "$MODE" = "docker" ]; then
  if ! have docker; then
    say "Installing Docker (get.docker.com)…"
    curl -fsSL https://get.docker.com | as_root sh
    as_root systemctl enable --now docker 2>/dev/null || true
    ok "Docker installed"
  else ok "Docker present"; fi
  if ! docker info >/dev/null 2>&1; then
    as_root systemctl start docker 2>/dev/null || true
    if ! docker info >/dev/null 2>&1 && as_root docker info >/dev/null 2>&1; then
      say "Adding $USER to the docker group (takes effect at your next login)…"
      as_root usermod -aG docker "$USER" 2>/dev/null || true
      warn "continuing this install with sudo for docker; log out/in to use docker without it."
    fi
  fi
  if ! have k3d; then
    say "Installing k3d ${K3D_VERSION} → $BIN…"
    curl -fsSL -o "$BIN/k3d" "https://github.com/k3d-io/k3d/releases/download/${K3D_VERSION}/k3d-linux-${ARCH}"
    chmod +x "$BIN/k3d"; ok "k3d ${K3D_VERSION}"
  else ok "k3d present"; fi
fi
# (service mode: `pctl server up` downloads and installs k3s itself.)

# ── 2. Build the TUI + install global commands ──────────────────────────────────────────────────
if [ -x "$ROOT/deploy/tui/pctl-tui" ]; then
  ok "dashboard binary bundled (no build needed)"
elif have go; then
  say "Building the dashboard…"
  ( cd "$ROOT/deploy/tui" && CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o pctl-tui . ) && ok "built deploy/tui/pctl-tui"
else
  warn "Go not found — the TUI will build on first launch instead."
fi

say "Installing global commands → $BIN (placecontext, pctl)…"
cat > "$BIN/placecontext" <<EOF
#!/usr/bin/env bash
# PlaceContext dashboard launcher (installed by deploy/install.sh).
export PATH="$BIN:\$PATH"
exec "$ROOT/deploy/pctl" tui "\$@"
EOF
cat > "$BIN/pctl" <<EOF
#!/usr/bin/env bash
export PATH="$BIN:\$PATH"
exec "$ROOT/deploy/pctl" "\$@"
EOF
chmod +x "$BIN/placecontext" "$BIN/pctl"
ok "installed 'placecontext' and 'pctl'"

case ":$PATH:" in
  *":$BIN:"*) : ;;
  *) warn "Add $BIN to your PATH, e.g.:  echo 'export PATH=\"$BIN:\$PATH\"' >> ~/.bashrc";;
esac

# ── 3. First-run setup: cluster + autostart ──────────────────────────────────────────────────────
if [ "$MODE" = "service" ]; then
  say "Setting up k3s as a system service…"
  "$ROOT/deploy/pctl" server up
  ok "k3s server up — it auto-starts on boot via its systemd service."
else
  say "Launching the cluster in Docker (k3d)…"
  "$ROOT/deploy/pctl" dev up
  say "Configuring autostart…"
  "$ROOT/deploy/pctl" autostart || warn "autostart not configured (see message above)."
fi

say "Done."
echo "  Type ${C_GREEN}placecontext${C_RESET} anywhere to open the dashboard."
[ "$LAUNCH" = 1 ] && [ -t 1 ] && exec "$BIN/placecontext"
