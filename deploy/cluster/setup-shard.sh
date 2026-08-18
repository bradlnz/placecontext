#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Quick shard setup — for adding a new Mac to an existing cluster.
# Run this on the Mac you want to add as a shard node.
#
# Usage:
#   bash setup-shard.sh --master-ip 100.64.0.10 --shard-index 1 --total-shards 2
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLUSTER_DIR="$HOME/.placecontext/cluster"
LOG_FILE="$CLUSTER_DIR/shard-setup.log"
MODEL="Qwen/Qwen3.5-4B"
PORT=8080

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
log()  { echo -e "${GREEN}[shard]${NC} $*" | tee -a "$LOG_FILE"; }
warn() { echo -e "${YELLOW}[warn]${NC} $*" | tee -a "$LOG_FILE"; }
err()  { echo -e "${RED}[error]${NC} $*" | tee -a "$LOG_FILE" >&2; exit 1; }

# ── Parse args ───────────────────────────────────────────────────────────────

MASTER_IP=""
SHARD_INDEX=""
TOTAL_SHARDS=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --master-ip)     MASTER_IP="$2"; shift 2 ;;
        --shard-index)   SHARD_INDEX="$2"; shift 2 ;;
        --total-shards)  TOTAL_SHARDS="$2"; shift 2 ;;
        --model)         MODEL="$2"; shift 2 ;;
        --port)          PORT="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 --master-ip <ip> --shard-index <n> --total-shards <n>"
            exit 0 ;;
        *) err "Unknown option: $1" ;;
    esac
done

[[ -z "$MASTER_IP" ]]    && err "Missing --master-ip"
[[ -z "$SHARD_INDEX" ]]  && err "Missing --shard-index"
[[ -z "$TOTAL_SHARDS" ]] && err "Missing --total-shards"

mkdir -p "$CLUSTER_DIR"

# ── Check platform ───────────────────────────────────────────────────────────

OS=$(uname -s)
ARCH=$(uname -m)
if [[ "$OS" == "Darwin" && "$ARCH" == "arm64" ]]; then
    PLATFORM="macos-arm64"
elif [[ "$OS" == "Linux" ]]; then
    PLATFORM="linux-$(uname -m)"
else
    err "Unsupported: $OS/$ARCH"
fi
log "Platform: $PLATFORM"

# ── Check Python ─────────────────────────────────────────────────────────────

if ! command -v python3 &>/dev/null; then
    err "python3 not found. Install Python 3.11+ first."
fi

PYVER=$(python3 -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')")
log "Python: $PYVER"

# ── Install deps ─────────────────────────────────────────────────────────────

log "Installing dependencies..."
if [[ "$PLATFORM" == "macos-arm64" ]]; then
    pip3 install --quiet mlx-lm fastapi uvicorn pydantic numpy 2>/dev/null || true
else
    pip3 install --quiet torch transformers fastapi uvicorn pydantic 2>/dev/null || true
fi

# ── Download server.py ───────────────────────────────────────────────────────

# Try to find server.py locally first, otherwise download from master
LOCAL_SERVER="$SCRIPT_DIR/../mac-shard/server.py"
if [[ -f "$LOCAL_SERVER" ]]; then
    cp "$LOCAL_SERVER" "$CLUSTER_DIR/server.py"
    log "Copied server.py from local"
else
    # Download from master node
    log "Downloading server.py from master ($MASTER_IP)..."
    curl -fsSL "http://$MASTER_IP:8080/health" &>/dev/null || warn "Master not reachable at $MASTER_IP:8080"

    err "Cannot find server.py. Copy deploy/mac-shard/server.py to $CLUSTER_DIR/"
fi

# ── Create service ───────────────────────────────────────────────────────────

if [[ "$PLATFORM" == "macos-arm64" ]]; then
    PLIST="$HOME/Library/LaunchAgents/com.placecontext.shard.plist"
    log "Creating launchd service at $PLIST..."

    mkdir -p "$HOME/Library/LaunchAgents"
    cat > "$PLIST" << EOF
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
        <string>$PORT</string>
        <string>--shard</string>
        <string>$SHARD_INDEX/$TOTAL_SHARDS</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>WorkingDirectory</key>
    <string>$CLUSTER_DIR</string>
    <key>StandardOutPath</key>
    <string>$CLUSTER_DIR/shard.log</string>
    <key>StandardErrorPath</key>
    <string>$CLUSTER_DIR/shard.err</string>
</dict>
</plist>
EOF

    launchctl unload "$PLIST" 2>/dev/null || true
    launchctl load "$PLIST"
    log "Launchd service started"

else
    # Linux: systemd
    SERVICE="/etc/systemd/system/placecontext-shard.service"
    log "Creating systemd service..."
    sudo tee "$SERVICE" > /dev/null << EOF
[Unit]
Description=PlaceContext Shard Server
After=network.target

[Service]
Type=simple
User=$(whoami)
WorkingDirectory=$CLUSTER_DIR
ExecStart=$(which python3) $CLUSTER_DIR/server.py --model $MODEL --port $PORT --shard $SHARD_INDEX/$TOTAL_SHARDS
Restart=always
RestartSec=5
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
EOF
    sudo systemctl daemon-reload
    sudo systemctl enable placecontext-shard
    sudo systemctl start placecontext-shard
    log "Systemd service started"
fi

# ── Verify ───────────────────────────────────────────────────────────────────

sleep 3
if curl -sf "http://localhost:$PORT/health" &>/dev/null; then
    HEALTH=$(curl -s "http://localhost:$PORT/health")
    log "Shard server healthy: $HEALTH"
else
    warn "Shard server not responding yet (may still be loading model)"
fi

log ""
log "Shard setup complete!"
log "  Shard: $SHARD_INDEX/$TOTAL_SHARDS"
log "  Port: $PORT"
log "  Master: $MASTER_IP"
log "  Log: $CLUSTER_DIR/shard.log"
log ""
log "Next: Add this node's IP to the master's ShardEndpoints config"
