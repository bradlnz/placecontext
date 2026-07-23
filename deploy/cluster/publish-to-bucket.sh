#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Publish cluster scripts to DigitalOcean Spaces bucket.
#
# Usage:
#   ./publish-to-bucket.sh --bucket placecontext-deploy --key <DO_KEY> --secret <DO_SECRET>
#
# Requires: s3cmd or aws CLI configured with DO Spaces endpoint.
# =============================================================================

BUCKET=""
DO_KEY=""
DO_SECRET=""
ENDPOINT="nyc3.digitaloceanspaces.com"
REGION="nyc3"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MAC_SHARD_DIR="$SCRIPT_DIR/../mac-shard"

RED='\033[0;31m'; GREEN='\033[0;32m'; NC='\033[0m'
log()  { echo -e "${GREEN}[publish]${NC} $*"; }
err()  { echo -e "${RED}[error]${NC} $*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
    case $1 in
        --bucket)   BUCKET="$2"; shift 2 ;;
        --key)      DO_KEY="$2"; shift 2 ;;
        --secret)   DO_SECRET="$2"; shift 2 ;;
        --endpoint) ENDPOINT="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 --bucket <name> --key <DO_KEY> --secret <DO_SECRET>"
            exit 0 ;;
        *) err "Unknown: $1" ;;
    esac
done

[[ -z "$BUCKET" ]]  && err "Missing --bucket"
[[ -z "$DO_KEY" ]]  && err "Missing --key"
[[ -z "$DO_SECRET" ]] && err "Missing --secret"

# ── Try s3cmd first, fallback to aws cli ─────────────────────────────────────

S3CMD_AVAILABLE=false
AWS_AVAILABLE=false

if command -v s3cmd &>/dev/null; then
    S3CMD_AVAILABLE=true
elif command -v aws &>/dev/null; then
    AWS_AVAILABLE=true
else
    err "Install s3cmd or aws CLI first"
fi

upload_file() {
    local local_path="$1"
    local remote_path="$2"
    local mime_type="${3:-application/octet-stream}"

    if [[ "$S3CMD_AVAILABLE" == "true" ]]; then
        s3cmd put --access_key="$DO_KEY" --secret_key="$DO_SECRET" \
            --host="${ENDPOINT}" --host-bucket="${BUCKET}.${ENDPOINT}" \
            --mime-type="$mime_type" \
            "$local_path" "s3://${BUCKET}/${remote_path}"
    else
        aws s3 cp "$local_path" "s3://${BUCKET}/${remote_path}" \
            --endpoint-url "https://${ENDPOINT}" \
            --acl public-read \
            --content-type "$mime_type"
    fi

    log "Uploaded: $remote_path"
}

# ── Upload files ─────────────────────────────────────────────────────────────

log "Publishing to s3://${BUCKET}/..."
log "Endpoint: $ENDPOINT"

# Cluster scripts
upload_file "$SCRIPT_DIR/install.sh"         "install.sh"         "text/x-shellscript"
upload_file "$SCRIPT_DIR/setup-shard.sh"     "setup-shard.sh"     "text/x-shellscript"

# Shard server
if [[ -f "$MAC_SHARD_DIR/server.py" ]]; then
    upload_file "$MAC_SHARD_DIR/server.py"   "server.py"          "text/x-python"
else
    log "Warning: $MAC_SHARD_DIR/server.py not found, skipping"
fi

# K8s manifests
K8S_DIR="$SCRIPT_DIR/../k3s"
if [[ -f "$K8S_DIR/placecontext.yaml" ]]; then
    upload_file "$K8S_DIR/placecontext.yaml" "placecontext.yaml"  "text/yaml"
fi

log ""
log "Publish complete! Users can now run:"
log "  curl -fsSL https://${BUCKET}.${ENDPOINT}/install.sh | bash -s -- --role shard --shard-index 1 --total-shards 2 --master-ip <IP>"
