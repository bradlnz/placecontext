#!/usr/bin/env bash
set -euo pipefail

: "${OPENSEARCH_SYNC_HOST:?set OPENSEARCH_SYNC_HOST to the SSH target}"
host="$OPENSEARCH_SYNC_HOST"
key="${SSH_KEY:-$HOME/.ssh/id_ed25519}"
ssh_opts=(-F /dev/null -o BatchMode=yes -i "$key")
scp_opts=(-F /dev/null -o BatchMode=yes -i "$key")
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

ssh "${ssh_opts[@]}" "$host" \
  'install -d -m 0755 /opt/placecontext/opensearch-sync-trigger /etc/placecontext'
scp -q "${scp_opts[@]}" \
  "$root/server.py" "$host:/opt/placecontext/opensearch-sync-trigger/server.py"
scp -q "${scp_opts[@]}" \
  "$root/opensearch-sync-trigger.service" "$host:/etc/systemd/system/opensearch-sync-trigger.service"
ssh "${ssh_opts[@]}" "$host" 'set -euo pipefail
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
