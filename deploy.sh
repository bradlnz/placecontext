#!/usr/bin/env bash
set -euo pipefail

ssh_key="${SSH_KEY:-$HOME/.ssh/id_ed25519}"
search_host="${OPENSEARCH_SYNC_HOST:-root@100.116.60.120}"
app_host="${PLACECONTEXT_DEPLOY_HOST:-root@100.81.205.22}"
da_jobs_root="${DA_JOBS_ROOT:-$HOME/code/ossen-reports/placecontext_jobs}"
da_jobs_source="$da_jobs_root"
da_jobs_temp=""
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
da_registry="$da_jobs_root/Ossen--6525de7d/da-pathway--8b8c0c02/map/council_requirements.json"

if [ "${ALLOW_STALE_DA_REGISTRY:-0}" = "1" ]; then
  da_jobs_temp="$(mktemp -d)"
  cp -R "$da_jobs_root/." "$da_jobs_temp/."
  da_jobs_source="$da_jobs_temp"
  da_registry="$da_jobs_source/Ossen--6525de7d/da-pathway--8b8c0c02/map/council_requirements.json"
  python3 - "$da_registry" <<'PY'
import datetime
import json
import sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as f:
    data = json.load(f)
data["source_checked"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
with open(path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2)
    f.write("\n")
PY
fi

for relative_path in "${da_bundle[@]}"; do
  if [ ! -f "$da_jobs_source/$relative_path" ]; then
    echo "Development-application deployment source is missing: $da_jobs_source/$relative_path" >&2
    exit 1
  fi
done

SSH_KEY="$ssh_key" OPENSEARCH_SYNC_HOST="$search_host" \
  ./deploy/opensearch-sync-trigger/install.sh

sync_token="$(ssh -F /dev/null -o BatchMode=yes -i "$ssh_key" "$search_host" \
  'set -e; . /etc/placecontext/opensearch-sync-trigger.env; printf %s "$SYNC_TRIGGER_TOKEN"')"
if [ -z "$sync_token" ]; then
  echo "OpenSearch sync trigger token is unavailable." >&2
  exit 1
fi

docker build -t registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest -f Dockerfile . 2>&1 | tail -3
docker push registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest 2>&1 | tail -3

printf '%s\n' "$sync_token" | ssh -F /dev/null -o BatchMode=yes -i "$ssh_key" "$app_host" '
read -r sync_token
set -euo pipefail
kubectl -n placecontext create secret generic opensearch-sync \
  --from-literal=PlaceContext__OpenSearch__SyncToken="$sync_token" \
  --dry-run=client -o yaml | kubectl apply -f -
unset sync_token
kubectl -n placecontext set env deployment/placecontext \
  PlaceContext__OpenSearch__SyncEndpoint=http://100.116.60.120:9340/v1/sync
kubectl -n placecontext set env deployment/placecontext --from=secret/opensearch-sync
kubectl -n placecontext patch deployment/placecontext --type=merge \
  -p "{\"spec\":{\"strategy\":{\"type\":\"RollingUpdate\",\"rollingUpdate\":{\"maxSurge\":0,\"maxUnavailable\":1}}}}"
kubectl -n placecontext set resources deployment/placecontext --containers=host \
  --requests=cpu=150m,memory=512Mi --limits=cpu=1500m,memory=2Gi
kubectl -n placecontext set resources deployment/placecontext --containers=cluster \
  --requests=cpu=50m,memory=256Mi --limits=cpu=500m,memory=512Mi
'

ssh -F /dev/null -o BatchMode=yes -i "$ssh_key" "$app_host" 'bash -s' <<'REMOTE'
set -euo pipefail
# The deployment may be pinned to an older release tag. Point it at the image
# this script just pushed before restarting so a successful push is actually deployed.
kubectl -n placecontext set image deployment/placecontext \
  host=registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest \
  cluster=registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest
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
tar -C "$da_jobs_source" -czf - "${da_bundle[@]}" \
  | ssh -F /dev/null -o BatchMode=yes -i "$ssh_key" "$app_host" '
      set -euo pipefail
      workdir="$(mktemp -d)"
      trap '\''rm -rf "$workdir"'\'' EXIT
      tar -xzf - -C "$workdir"
      python3 "$workdir/deploy_da_application.py" "$workdir"
'

if [ -n "$da_jobs_temp" ] && [ -d "$da_jobs_temp" ]; then
  rm -rf "$da_jobs_temp"
fi

unset sync_token
