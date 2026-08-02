#!/usr/bin/env bash
set -euo pipefail

host="${OPENSEARCH_SYNC_HOST:-root@100.116.60.120}"
key="${SSH_KEY:-$HOME/.ssh/id_ed25519}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ssh -o BatchMode=yes -i "$key" "$host" \
  'install -d -m 0755 /opt/placecontext/opensearch-sync-trigger /etc/placecontext'
scp -q -o BatchMode=yes -i "$key" \
  "$root/server.py" "$host:/opt/placecontext/opensearch-sync-trigger/server.py"
scp -q -o BatchMode=yes -i "$key" \
  "$root/opensearch-sync-trigger.service" "$host:/etc/systemd/system/opensearch-sync-trigger.service"
ssh -o BatchMode=yes -i "$key" "$host" 'set -euo pipefail
chmod 0755 /opt/placecontext/opensearch-sync-trigger/server.py
if [ ! -s /etc/placecontext/opensearch-sync-trigger.env ]; then
  umask 077
  token="$(openssl rand -hex 32)"
  printf "SYNC_TRIGGER_TOKEN=%s\n" "$token" > /etc/placecontext/opensearch-sync-trigger.env
fi
chmod 0600 /etc/placecontext/opensearch-sync-trigger.env
systemctl daemon-reload
systemctl enable --now opensearch-sync-trigger.service
systemctl is-active --quiet opensearch-sync-trigger.service
'
