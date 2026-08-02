#!/usr/bin/env bash
set -euo pipefail

ssh_key="${SSH_KEY:-$HOME/.ssh/id_ed25519}"
search_host="${OPENSEARCH_SYNC_HOST:-root@100.116.60.120}"
app_host="${PLACECONTEXT_DEPLOY_HOST:-root@100.81.205.22}"

SSH_KEY="$ssh_key" OPENSEARCH_SYNC_HOST="$search_host" \
  ./deploy/opensearch-sync-trigger/install.sh

sync_token="$(ssh -o BatchMode=yes -i "$ssh_key" "$search_host" \
  'set -e; . /etc/placecontext/opensearch-sync-trigger.env; printf %s "$SYNC_TRIGGER_TOKEN"')"
if [ -z "$sync_token" ]; then
  echo "OpenSearch sync trigger token is unavailable." >&2
  exit 1
fi

docker build -t registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest -f Dockerfile . 2>&1 | tail -3
docker push registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest 2>&1 | tail -3

{
  printf '%s\n' "$sync_token"
  cat <<'REMOTE'
read -r sync_token
set -euo pipefail
kubectl -n placecontext create secret generic opensearch-sync \
  --from-literal=PlaceContext__OpenSearch__SyncToken="$sync_token" \
  --dry-run=client -o yaml | kubectl apply -f -
unset sync_token
kubectl -n placecontext set env deployment/placecontext \
  PlaceContext__OpenSearch__SyncEndpoint=http://100.116.60.120:9340/v1/sync
kubectl -n placecontext set env deployment/placecontext --from=secret/opensearch-sync
kubectl -n placecontext rollout restart deployment/placecontext
sleep 30
kubectl -n placecontext get pods
# Kill orphaned runs left by the old deployment.
pod="$(kubectl -n placecontext get pod -l app=placecontext-db -o jsonpath="{.items[0].metadata.name}" 2>/dev/null)"
if [ -n "$pod" ]; then
  echo "UPDATE job_runs SET \"Status\" = 'Failed', \"FinishedAt\" = now() WHERE \"Status\" IN ('Queued', 'Running'); UPDATE chain_runs SET \"Status\" = 'Failed', \"FinishedAt\" = now() WHERE \"Status\" IN ('Queued', 'Running');" \
    | kubectl -n placecontext exec "$pod" -- psql -U postgres -d placecontext
fi
REMOTE
} | ssh -o BatchMode=yes -i "$ssh_key" "$app_host" 'bash -s' 2>&1

unset sync_token
