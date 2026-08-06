#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# PlaceContext Node Installer
# Installs the PlaceContext Host application on a Linux node (K3s master or
# standalone). This is the general node setup — NOT the shard server setup.
#
# Usage:
#   curl -fsSL https://<bucket>.digitaloceanspaces.com/install-node.sh | bash -s -- --role master
#   curl -fsSL https://<bucket>.digitaloceanspaces.com/install-node.sh | bash -s -- --role worker
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUCKET_URL="${PLACECONTEXT_BUCKET:-https://placecontext-deploy.nyc3.digitaloceanspaces.com}"
INSTALL_DIR="$HOME/.placecontext"
CONFIG_DIR="$INSTALL_DIR/config"
LOG_FILE="$INSTALL_DIR/install-node.log"
APP_PORT="${PLACECONTEXT_PORT:-7700}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log()  { echo -e "${GREEN}[node]${NC} $*" | tee -a "$LOG_FILE"; }
warn() { echo -e "${YELLOW}[warn]${NC} $*" | tee -a "$LOG_FILE"; }
err()  { echo -e "${RED}[error]${NC} $*" | tee -a "$LOG_FILE" >&2; exit 1; }
info() { echo -e "${BLUE}[info]${NC} $*" | tee -a "$LOG_FILE"; }

# ── Parse args ───────────────────────────────────────────────────────────────

ROLE=""
TAILSCALE_AUTH_KEY=""
DB_HOST=""
DB_NAME="placecontext"
DB_USER="placecontext"
DB_PASS=""
REDIS_HOST=""
API_KEY=""
OTEL_ENDPOINT=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --role)              ROLE="$2"; shift 2 ;;
        --tailscale-key)     TAILSCALE_AUTH_KEY="$2"; shift 2 ;;
        --db-host)           DB_HOST="$2"; shift 2 ;;
        --db-name)           DB_NAME="$2"; shift 2 ;;
        --db-user)           DB_USER="$2"; shift 2 ;;
        --db-pass)           DB_PASS="$2"; shift 2 ;;
        --redis-host)        REDIS_HOST="$2"; shift 2 ;;
        --api-key)           API_KEY="$2"; shift 2 ;;
        --otel-endpoint)     OTEL_ENDPOINT="$2"; shift 2 ;;
        --bucket)            BUCKET_URL="$2"; shift 2 ;;
        --port)              APP_PORT="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 --role <master|worker> [options]"
            echo ""
            echo "Required:"
            echo "  --role              master or worker"
            echo ""
            echo "Recommended:"
            echo "  --tailscale-key     Tailscale auth key for node registration"
            echo "  --db-host           Postgres host (default: localhost)"
            echo "  --db-pass           Postgres password"
            echo "  --redis-host        Redis host (for job run caching)"
            echo "  --api-key           API key for management API"
            echo "  --otel-endpoint     OpenTelemetry collector endpoint"
            echo ""
            echo "Optional:"
            echo "  --db-name           Postgres database name (default: placecontext)"
            echo "  --db-user           Postgres user (default: placecontext)"
            echo "  --bucket            DO Spaces bucket URL"
            echo "  --port              App port (default: 7700)"
            exit 0
            ;;
        *) err "Unknown option: $1" ;;
    esac
done

[[ -z "$ROLE" ]] && err "Missing --role (master or worker)"

mkdir -p "$CONFIG_DIR"

# ── Detect platform ──────────────────────────────────────────────────────────

OS=$(uname -s)
ARCH=$(uname -m)

if [[ "$OS" != "Linux" ]]; then
    err "This script is for Linux nodes only. Use setup-shard.sh for Mac shard servers."
fi

log "Platform: Linux/$ARCH"

# ── Check prerequisites ─────────────────────────────────────────────────────

check_prereqs() {
    log "Checking prerequisites..."

    # Check for .NET 8+
    if command -v dotnet &>/dev/null; then
        DOTNET_VER=$(dotnet --version 2>/dev/null || echo "unknown")
        log ".NET SDK: $DOTNET_VER"
    else
        warn ".NET not found — will install via Microsoft repo"
    fi

    # Check for Docker (needed for K3s if using containerd)
    if ! command -v docker &>/dev/null && ! command -v k3s &>/dev/null; then
        warn "Neither Docker nor K3s found"
    fi

    # Check for Tailscale
    if command -v tailscale &>/dev/null; then
        TS_STATUS=$(tailscale status 2>/dev/null || echo "not connected")
        log "Tailscale: $TS_STATUS"
    else
        warn "Tailscale not installed — cluster nodes need it for inter-node communication"
    fi
}

# ── Install system dependencies ──────────────────────────────────────────────

install_system_deps() {
    log "Installing system dependencies..."

    if command -v apt-get &>/dev/null; then
        sudo apt-get update -qq
        sudo apt-get install -y -qq curl gnupg apt-transport-https ca-certificates lsb-release 2>/dev/null || true
    elif command -v yum &>/dev/null; then
        sudo yum install -y curl gnupg2 ca-certificates 2>/dev/null || true
    fi

    log "System dependencies installed"
}

# ── Install .NET SDK ─────────────────────────────────────────────────────────

install_dotnet() {
    if command -v dotnet &>/dev/null; then
        log ".NET already installed: $(dotnet --version)"
        return
    fi

    log "Installing .NET 8 SDK..."

    # Microsoft package repo
    if command -v apt-get &>/dev/null; then
        wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
        sudo dpkg -i /tmp/packages-microsoft-prod.deb
        sudo apt-get update -qq
        sudo apt-get install -y dotnet-sdk-8.0
    elif command -v yum &>/dev/null; then
        sudo rpm -Uvh https://packages.microsoft.com/config/centos/8/packages-microsoft-prod.rpm
        sudo yum install -y dotnet-sdk-8.0
    else
        # Fallback: install via official script
        curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir $HOME/.dotnet
        export PATH="$HOME/.dotnet:$PATH"
    fi

    log ".NET installed: $(dotnet --version)"
}

# ── Install Tailscale ────────────────────────────────────────────────────────

install_tailscale() {
    if command -v tailscale &>/dev/null; then
        log "Tailscale already installed"
        return
    fi

    log "Installing Tailscale..."
    curl -fsSL https://tailscale.com/install.sh | sh

    if [[ -n "$TAILSCALE_AUTH_KEY" ]]; then
        log "Connecting to Tailscale network..."
        sudo tailscale up --authkey="$TAILSCALE_AUTH_KEY"
        local my_ip
        my_ip=$(tailscale ip -4 2>/dev/null || echo "unknown")
        log "Tailscale IP: $my_ip"
    else
        warn "No --tailscale-key provided. Run 'tailscale up' manually to join your network."
    fi
}

# ── Install K3s (master role) ───────────────────────────────────────────────

install_k3s() {
    if command -v k3s &>/dev/null; then
        log "K3s already installed: $(k3s --version 2>/dev/null | head -1)"
        return
    fi

    log "Installing K3s..."
    curl -sfL https://get.k3s.io | sh -

    # Set up kubeconfig for current user
    mkdir -p "$HOME/.kube"
    sudo cp /etc/rancher/k3s/k3s.yaml "$HOME/.kube/config"
    sudo chown $(whoami):$(whoami) "$HOME/.kube/config"
    chmod 600 "$HOME/.kube/config"

    export KUBECONFIG="$HOME/.kube/config"
    log "K3s installed"
}

# ── Download PlaceContext Host ───────────────────────────────────────────────

download_app() {
    log "Downloading PlaceContext Host application..."

    local app_dir="$INSTALL_DIR/app"
    mkdir -p "$app_dir"

    # Download the published app archive from DO bucket
    if curl -fsSL "$BUCKET_URL/placecontext-host.tar.gz" -o "$app_dir/app.tar.gz" 2>/dev/null; then
        tar -xzf "$app_dir/app.tar.gz" -C "$app_dir"
        log "Application downloaded and extracted"
    else
        warn "Could not download pre-built app from bucket"
        warn "You'll need to build from source: dotnet publish src/PlaceContext.Host -c Release"
    fi
}

# ── Create systemd service ───────────────────────────────────────────────────

create_service() {
    local service_file="/etc/systemd/system/placecontext-host.service"
    log "Creating systemd service..."

    local app_dir="$INSTALL_DIR/app"
    local app_exe="$app_dir/PlaceContext.Host"

    # If the app binary doesn't exist, use dotnet run
    if [[ -f "$app_exe" ]]; then
        local exec_start="$app_exe"
    else
        local exec_start="dotnet $app_dir/PlaceContext.Host.dll"
    fi

    # Build environment variables
    local env_vars=""
    [[ -n "$DB_HOST" ]]     && env_vars+="Environment=ConnectionStrings__Postgres=Host=$DB_HOST;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS\n"
    [[ -n "$REDIS_HOST" ]]  && env_vars+="Environment=PlaceContext__Redis__Host=$REDIS_HOST\n"
    [[ -n "$API_KEY" ]]     && env_vars+="Environment=PlaceContext__ApiKey=$API_KEY\n"
    [[ -n "$OTEL_ENDPOINT" ]] && env_vars+="Environment=OTEL_EXPORTER_OTLP_ENDPOINT=$OTEL_ENDPOINT\n"
    env_vars+="Environment=ASPNETCORE_URLS=http://0.0.0.0:$APP_PORT\n"
    env_vars+="Environment=ASPNETCORE_ENVIRONMENT=Production\n"

    sudo tee "$service_file" > /dev/null << EOF
[Unit]
Description=PlaceContext Host Application
After=network.target

[Service]
Type=simple
User=$(whoami)
WorkingDirectory=$app_dir
ExecStart=$exec_start
Restart=always
RestartSec=5
$env_vars

[Install]
WantedBy=multi-user.target
EOF

    sudo systemctl daemon-reload
    sudo systemctl enable placecontext-host
    sudo systemctl start placecontext-host

    log "Systemd service created and started"
}

# ── Apply K8s manifests (master role) ───────────────────────────────────────

ensure_placecontext_namespace() {
    if kubectl get namespace placecontext >/dev/null 2>&1; then
        return
    fi

    log "Creating namespace placecontext..."
    kubectl create namespace placecontext >/dev/null
}

apply_k8s_manifests() {
    if [[ "$ROLE" != "master" ]]; then
        return
    fi

    log "Applying K8s manifests..."

    export KUBECONFIG="$HOME/.kube/config"

    # Download manifests from bucket
    if curl -fsSL "$BUCKET_URL/placecontext.yaml" -o "$CONFIG_DIR/placecontext.yaml" 2>/dev/null; then
        ensure_placecontext_namespace
        kubectl apply -f "$CONFIG_DIR/placecontext.yaml"
        log "K8s manifests applied"
    else
        warn "Could not download K8s manifests from bucket"
        warn "Apply manually: kubectl apply -f deploy/k3s/placecontext.yaml"
    fi
}

# ── Create node config ───────────────────────────────────────────────────────

create_node_config() {
    log "Creating node config at $CONFIG_DIR/node.yaml..."

    local my_ip
    my_ip=$(tailscale ip -4 2>/dev/null || hostname -I 2>/dev/null | awk '{print $1}' || echo "unknown")

    cat > "$CONFIG_DIR/node.yaml" << EOF
# PlaceContext Node Configuration
# Generated by install-node.sh on $(date -u +"%Y-%m-%dT%H:%M:%SZ")

node:
  role: $ROLE
  ip: $my_ip
  port: $APP_PORT
  hostname: $(hostname)

database:
  host: ${DB_HOST:-localhost}
  name: $DB_NAME
  user: $DB_USER

redis:
  host: ${REDIS_HOST:-}

tailscale:
  configured: $([ -n "$TAILSCALE_AUTH_KEY" ] && echo "true" || echo "false")

otel:
  endpoint: ${OTEL_ENDPOINT:-}
EOF

    log "Node config created"
}

# ── Health check ─────────────────────────────────────────────────────────────

verify_install() {
    log "Verifying installation..."

    sleep 3

    # Check systemd service
    if systemctl is-active --quiet placecontext-host; then
        log "PlaceContext Host service: running"
    else
        warn "PlaceContext Host service: not running (check logs: journalctl -u placecontext-host)"
    fi

    # Check app health endpoint
    if curl -sf "http://localhost:$APP_PORT/healthz" &>/dev/null; then
        log "Health endpoint: OK (http://localhost:$APP_PORT/healthz)"
    else
        warn "Health endpoint: not responding (may still be starting up)"
    fi

    # Check K3s (master only)
    if [[ "$ROLE" == "master" ]]; then
        if kubectl get nodes &>/dev/null; then
            log "K3s cluster: healthy"
            kubectl get nodes
        else
            warn "K3s: not responding"
        fi
    fi
}

# ── Main ────────────────────────────────────────────────────────────────────

main() {
    log "PlaceContext Node Installer"
    log "==========================="
    log "Role: $ROLE"
    log "Port: $APP_PORT"
    log "Bucket: $BUCKET_URL"

    check_prereqs
    install_system_deps
    install_dotnet
    install_tailscale

    if [[ "$ROLE" == "master" ]]; then
        install_k3s
    fi

    download_app
    create_service
    apply_k8s_manifests
    create_node_config
    verify_install

    log ""
    log "Installation complete!"
    log "  App: http://localhost:$APP_PORT"
    log "  Logs: journalctl -u placecontext-host -f"
    log "  Config: $CONFIG_DIR/node.yaml"
    log ""
    if [[ "$ROLE" == "master" ]]; then
        log "Next steps:"
        log "  1. Add shard nodes: curl -fsSL $BUCKET_URL/setup-shard.sh | bash -s -- --master-ip $(tailscale ip -4 2>/dev/null || echo '<this-ip>') --shard-index 0 --total-shards 2"
        log "  2. Configure ShardEndpoints in appsettings.json"
        log "  3. Restart: sudo systemctl restart placecontext-host"
    else
        log "Next steps:"
        log "  1. Register this node with the master"
        log "  2. Configure shard endpoints if running inference"
    fi
}

main "$@"
