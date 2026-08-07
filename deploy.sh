#!/usr/bin/env bash
set -euo pipefail

ssh_key="${SSH_KEY:-$HOME/.ssh/id_ed25519}"
search_host="${OPENSEARCH_SYNC_HOST:-root@100.116.60.120}"
app_host="${PLACECONTEXT_DEPLOY_HOST:-root@100.81.205.22}"
da_jobs_root="${DA_JOBS_ROOT:-$HOME/code/ossen-reports/placecontext_jobs}"
da_bundle=(
  deploy_da_application.py
  Ossen--6525de7d/da-intake--8b8c0c01/map/main.py
  Ossen--6525de7d/da-pathway--8b8c0c02/map/main.py
  Ossen--6525de7d/da-pathway--8b8c0c02/map/council_requirements.json
  Ossen--6525de7d/da-readiness--8b8c0c03/map/main.py
  Ossen--6525de7d/da-pdf--8b8c0c05/map/main.py
  Ossen--6525de7d/da-pdf--8b8c0c05/map/requirements.txt
  Ossen--6525de7d/council-registry--447808c5/map/main.py
  Ossen--6525de7d/overlays--74719f98/map/main.py
)

for relative_path in "${da_bundle[@]}"; do
  if [ ! -f "$da_jobs_root/$relative_path" ]; then
    echo "Development-application deployment source is missing: $da_jobs_root/$relative_path" >&2
    exit 1
  fi
done

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

docker build -t registry.digitalocean.com/ctrlsignalregistryimg/placecontext-customer-portal:latest -f Dockerfile.customer-portal . 2>&1 | tail -3
docker push registry.digitalocean.com/ctrlsignalregistryimg/placecontext-customer-portal:latest 2>&1 | tail -3

printf '%s\n' "$sync_token" | ssh -o BatchMode=yes -i "$ssh_key" "$app_host" '
read -r sync_token
set -euo pipefail
kubectl -n placecontext create secret generic opensearch-sync \
  --from-literal=PlaceContext__OpenSearch__SyncToken="$sync_token" \
  --dry-run=client -o yaml | kubectl apply -f -
unset sync_token
kubectl -n placecontext set env deployment/placecontext \
  PlaceContext__OpenSearch__SyncEndpoint=http://100.116.60.120:9340/v1/sync

# Keep customer-portal API authentication aligned with portal deployments by sourcing the same
# shared secret key into the host env from customer-portal-secrets/core-api-key.
customer_portal_api_key_patch=$(cat <<'PATCH'
{
  "spec": {
    "template": {
      "spec": {
        "containers": [
          {
            "name": "host",
            "env": [
              {
                "name": "PlaceContext__CustomerPortal__ApiKey",
                "valueFrom": {
                  "secretKeyRef": {
                    "name": "customer-portal-secrets",
                    "key": "core-api-key",
                    "optional": true
                  }
                }
              }
            ]
          }
        ]
      }
    }
  }
}
PATCH
)
kubectl -n placecontext patch deployment/placecontext --type=strategic -p "$customer_portal_api_key_patch"
kubectl -n placecontext set env deployment/placecontext --from=secret/opensearch-sync
'

ssh -o BatchMode=yes -i "$ssh_key" "$app_host" 'bash -s' <<'REMOTE'
set -euo pipefail
# The deployment may be pinned to an older release tag. Point it at the image
# this script just pushed before restarting so a successful push is actually deployed.
kubectl -n placecontext set image deployment/placecontext \
  host=registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest
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

printf 'Deploying council development-application jobs and chain...\n'
tar -C "$da_jobs_root" -czf - "${da_bundle[@]}" \
  | ssh -o BatchMode=yes -i "$ssh_key" "$app_host" '
      set -euo pipefail
      workdir="$(mktemp -d)"
      trap '\''rm -rf "$workdir"'\'' EXIT
      tar -xzf - -C "$workdir"
      python3 "$workdir/deploy_da_application.py" "$workdir"
    '

unset sync_token
