#!/usr/bin/env bash
#
# install.sh — one command to install and launch PlaceContext.
#
# It installs the dependencies it can without root, installs a global `placecontext`
# command (and `pctl`), then on this first run sets everything up: brings up the
# cluster, applies your activation key, and configures the cluster to start on boot.
# After that, just type `placecontext` anywhere to open the dashboard.
#
# Usage:
#   ./deploy/install.sh [--activation-key KEY] [--prod] [--no-launch]
#
#   --activation-key KEY  Activation key (required for --prod; optional for dev).
#   --prod                Install a real k3s server (systemd service) instead of the
#                         local k3d dev cluster. Run with sudo.
#   --no-launch           Set everything up but don't open the TUI at the end.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="${PCTL_INSTALL_BIN:-$HOME/.local/bin}"
ARCH="$(uname -m)"; case "$ARCH" in x86_64) ARCH=amd64;; aarch64|arm64) ARCH=arm64;; esac
K3D_VERSION="${K3D_VERSION:-v5.7.4}"

ACTIVATION_KEY=""; MODE="dev"; LAUNCH=1
while [ $# -gt 0 ]; do
  case "$1" in
    --activation-key) ACTIVATION_KEY="$2"; shift 2;;
    --prod)           MODE="prod"; shift;;
    --no-launch)      LAUNCH=0; shift;;
    -h|--help)        grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 2;;
  esac
done

C_CYAN=$'\033[1;36m'; C_GREEN=$'\033[1;32m'; C_YELLOW=$'\033[1;33m'; C_RESET=$'\033[0m'
say()  { printf '\n%s==>%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
ok()   { printf '%s✓%s %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn() { printf '%s!%s %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
die()  { printf '\033[1;31m✗\033[0m %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

mkdir -p "$BIN"
export PATH="$BIN:$PATH"

# ── 1. Dependencies ──────────────────────────────────────────────────────────────────────────────
say "Checking dependencies…"
if ! have kubectl; then
  say "Installing kubectl → $BIN…"
  ver="$(curl -fsSL https://dl.k8s.io/release/stable.txt)"
  curl -fsSL -o "$BIN/kubectl" "https://dl.k8s.io/release/${ver}/bin/linux/${ARCH}/kubectl"
  chmod +x "$BIN/kubectl"; ok "kubectl ${ver}"
else ok "kubectl present"; fi

if [ "$MODE" = "dev" ]; then
  have docker || die "Docker is required for the dev cluster. Install Docker and re-run."
  if ! have k3d; then
    say "Installing k3d ${K3D_VERSION} → $BIN…"
    curl -fsSL -o "$BIN/k3d" "https://github.com/k3d-io/k3d/releases/download/${K3D_VERSION}/k3d-linux-${ARCH}"
    chmod +x "$BIN/k3d"; ok "k3d ${K3D_VERSION}"
  else ok "k3d present"; fi
fi

# ── 2. Build the TUI + install global commands ──────────────────────────────────────────────────
if have go; then
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

# ── 3. First-run setup: cluster + activation + autostart ─────────────────────────────────────────
if [ "$MODE" = "prod" ]; then
  [ -n "$ACTIVATION_KEY" ] || die "--prod requires --activation-key <KEY>"
  say "Setting up the k3s server (activation enforced)…"
  "$ROOT/deploy/pctl" server up --activation-key "$ACTIVATION_KEY"
  ok "k3s server up — it auto-starts on boot via its systemd service."
else
  say "Launching the dev cluster…"
  PCTL_ACTIVATION_KEY="${ACTIVATION_KEY:-dev-local-unenforced}" "$ROOT/deploy/pctl" dev up
  say "Configuring autostart…"
  "$ROOT/deploy/pctl" autostart || warn "autostart not configured (see message above)."
fi

say "Done."
echo "  Type ${C_GREEN}placecontext${C_RESET} anywhere to open the dashboard."
[ "$LAUNCH" = 1 ] && [ -t 1 ] && exec "$BIN/placecontext"
