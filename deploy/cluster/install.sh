#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# PlaceContext Cluster Installer
# Downloads from DO bucket and sets up either a master node or shard node.
#
# Usage:
#   curl -fsSL https://<bucket>.digitaloceanspaces.com/install.sh | bash -s -- --role master
#   curl -fsSL https://<bucket>.digitaloceanspaces.com/install.sh | bash -s -- --role shard
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUCKET_URL="${PLACECONTEXT_BUCKET:-https://placecontext-deploy.nyc3.digitaloceanspaces.com}"
CLUSTER_DIR="$HOME/.placecontext/cluster"
CONFIG_FILE="$CLUSTER_DIR/config.yaml"
LOG_FILE="$CLUSTER_DIR/install.log"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() { echo -e "${GREEN}[install]${NC} $*" | tee -a "$LOG_FILE"; }
warn() { echo -e "${YELLOW}[warn]${NC} $*" | tee -a "$LOG_FILE"; }
err() { echo -e "${RED}[error]${NC} $*" | tee -a "$LOG_FILE" >&2; exit 1; }

# ── Parse args ───────────────────────────────────────────────────────────────

ROLE=""
SHARD_INDEX=""
TOTAL_SHARDS=""
MASTER_IP=""
MODEL="Qwen/Qwen3.5-4B"

while [[ $# -gt 0 ]]; do
    case $1 in
        --role) ROLE="$2"; shift 2 ;;
        --shard-index) SHARD_INDEX="$2"; shift 2 ;;
        --total-shards) TOTAL_SHARDS="$2"; shift 2 ;;
        --master-ip) MASTER_IP="$2"; shift 2 ;;
        --model) MODEL="$2"; shift 2 ;;
        --bucket) BUCKET_URL="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 --role <master|shard> [options]"
            echo "  --role          master or shard (required)"
            echo "  --shard-index   Shard index (0-based, required for shard role)"
            echo "  --total-shards  Total number of shards (required for shard role)"
            echo "  --master-ip     Tailscale IP of the master node (required for shard role)"
            echo "  --model         Model path (default: Qwen/Qwen3.5-4B)"
            echo "  --bucket        DO Spaces bucket URL"
            exit 0
            ;;
        *) err "Unknown option: $1" ;;
    esac
done

[[ -z "$ROLE" ]] && err "Missing --role (master or shard)"
[[ "$ROLE" == "shard" && -z "$SHARD_INDEX" ]] && err "Missing --shard-index for shard role"
[[ "$ROLE" == "shard" && -z "$TOTAL_SHARDS" ]] && err "Missing --total-shards for shard role"
[[ "$ROLE" == "shard" && -z "$MASTER_IP" ]] && err "Missing --master-ip for shard role"

mkdir -p "$CLUSTER_DIR"

# ── Detect platform ──────────────────────────────────────────────────────────

detect_platform() {
    local os arch
    os="$(uname -s)"
    arch="$(uname -m)"

    case "$os" in
        Darwin) PLATFORM="macos" ;;
        Linux)  PLATFORM="linux" ;;
        *)      err "Unsupported OS: $os" ;;
    esac

    case "$arch" in
        arm64|aarch64) ARCH="arm64" ;;
        x86_64|amd64)  ARCH="x86_64" ;;
        *)             err "Unsupported architecture: $arch" ;;
    esac

    log "Platform: $PLATFORM/$ARCH"
}

# ── Check prerequisites ─────────────────────────────────────────────────────

check_prereqs() {
    log "Checking prerequisites..."

    if ! command -v python3 &>/dev/null; then
        err "python3 not found. Install Python 3.11+ first."
    fi

    local pyver
    pyver=$(python3 -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')")
    if [[ "$(echo "$pyver < 3.11" | bc -l)" == "1" ]]; then
        err "Python $pyver found, need 3.11+. Use the fine-tuning venv or install newer Python."
    fi
    log "Python: $pyver"

    if ! command -v pip3 &>/dev/null; then
        warn "pip3 not found, attempting to install..."
        python3 -m ensurepip --default-pip 2>/dev/null || true
    fi

    # Check for Tailscale
    if ! command -v tailscale &>/dev/null; then
        warn "Tailscale not found. Cluster nodes must be on the same Tailscale network."
        warn "Install: https://tailscale.com/download"
    fi
}

# ── Install Python dependencies ──────────────────────────────────────────────

install_deps() {
    log "Installing Python dependencies..."

    if [[ "$PLATFORM" == "macos" ]]; then
        # Check for mlx-lm (Metal-native inference)
        pip3 install --quiet mlx-lm 2>/dev/null || {
            warn "mlx-lm install failed (expected on non-Metal hardware)"
        }
        # Install FastAPI + uvicorn
        pip3 install --quiet fastapi uvicorn pydantic numpy 2>/dev/null || true
    else
        # Linux: torch + transformers
        pip3 install --quiet torch transformers fastapi uvicorn pydantic 2>/dev/null || true
    fi

    log "Dependencies installed"
}

# ── Download shard server from bucket ────────────────────────────────────────

download_shard_server() {
    log "Downloading shard server from $BUCKET_URL..."
    local dest="$CLUSTER_DIR/server.py"
    curl -fsSL "$BUCKET_URL/server.py" -o "$dest" || err "Failed to download server.py"
    chmod +x "$dest"
    log "Shard server saved to $dest"
}

# ── Setup shard node ────────────────────────────────────────────────────────

setup_shard() {
    log "Setting up shard node ($SHARD_INDEX/$TOTAL_SHARDS)..."

    download_shard_server

    # Create systemd service (Linux) or launchd plist (macOS)
    if [[ "$PLATFORM" == "linux" ]]; then
        create_systemd_service
    elif [[ "$PLATFORM" == "macos" ]]; then
        create_launchd_service
    fi

    log "Shard node setup complete!"
    log "Model: $MODEL"
    log "Shard: $SHARD_INDEX/$TOTAL_SHARDS"
    log "Master: $MASTER_IP"
}

# ── Create systemd service (Linux) ──────────────────────────────────────────

create_systemd_service() {
    local service_file="/etc/systemd/system/placecontext-shard.service"
    log "Creating systemd service..."

    sudo tee "$service_file" > /dev/null << EOF
[Unit]
Description=PlaceContext Shard Server
After=network.target

[Service]
Type=simple
User=$(whoami)
WorkingDirectory=$CLUSTER_DIR
ExecStart=$(which python3) $CLUSTER_DIR/server.py \\
    --model $MODEL \\
    --port 8080 \\
    --shard $SHARD_INDEX/$TOTAL_SHARDS
Restart=always
RestartSec=5
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
EOF

    sudo systemctl daemon-reload
    sudo systemctl enable placecontext-shard
    sudo systemctl start placecontext-shard
    log "Systemd service created and started"
}

# ── Create launchd service (macOS) ──────────────────────────────────────────

create_launchd_service() {
    local plist_file="$HOME/Library/LaunchAgents/com.placecontext.shard.plist"
    log "Creating launchd service..."

    mkdir -p "$HOME/Library/LaunchAgents"

    cat > "$plist_file" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.placecontext.shard</string>
    <key>ProgramArguments</key>
    <array>
        <string>$(which python3)</string>
        <string>$CLUSTER_DIR/server.py</string>
        <string>--model</string>
        <string>$MODEL</string>
        <string>--port</string>
        <string>8080</string>
        <string>--shard</string>
        <string>$SHARD_INDEX/$TOTAL_SHARDS</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>WorkingDirectory</key>
    <string>$CLUSTER_DIR</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PYTHONUNBUFFERED</key>
        <string>1</string>
    </dict>
</dict>
</plist>
EOF

    launchctl unload "$plist_file" 2>/dev/null || true
    launchctl load "$plist_file"
    log "Launchd service created and started"
}

# ── Setup master node ───────────────────────────────────────────────────────

setup_master() {
    log "Setting up master node..."

    # Check for k3s
    if ! command -v kubectl &>/dev/null; then
        log "Installing K3s..."
        curl -sfL https://get.k3s.io | sh -
        export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
        log "K3s installed"
    fi

    # Download deployment manifests
    log "Downloading K8s manifests..."
    curl -fsSL "$BUCKET_URL/placecontext.yaml" -o "$CLUSTER_DIR/placecontext.yaml" || true

    # Apply manifests
    if [[ -f "$CLUSTER_DIR/placecontext.yaml" ]]; then
        kubectl apply -f "$CLUSTER_DIR/placecontext.yaml"
        log "K8s manifests applied"
    fi

    # Create cluster config
    create_cluster_config

    log "Master node setup complete!"
    log "Configure shard nodes to connect to this master at: $(tailscale ip -4 2>/dev/null || echo '<this-node-ip>')"
}

# ── Create cluster config ───────────────────────────────────────────────────

create_cluster_config() {
    log "Creating cluster config at $CONFIG_FILE..."

    cat > "$CONFIG_FILE" << EOF
# PlaceContext Cluster Configuration
# Generated by install.sh on $(date -u +"%Y-%m-%dT%H:%M:%SZ")

model: "$MODEL"

# Shard servers (add entries as nodes come online)
shard_servers: []
  # - ip: "100.x.x.x"
  #   port: 8080
  #   name: "node-name"
  #   platform: "macos-arm64"
  #   layers: "0-17"
EOF

    log "Cluster config created at $CONFIG_FILE"
}

# ── Main ────────────────────────────────────────────────────────────────────

main() {
    log "PlaceContext Cluster Installer"
    log "=============================="
    log "Role: $ROLE"
    log "Bucket: $BUCKET_URL"

    detect_platform
    check_prereqs
    install_deps

    case "$ROLE" in
        master) setup_master ;;
        shard)  setup_shard ;;
        *)      err "Invalid role: $ROLE (use 'master' or 'shard')" ;;
    esac

    log ""
    log "Installation complete!"
    log "Logs: $LOG_FILE"
    log "Config: $CONFIG_FILE"
}

main "$@"
