docker build -t registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest -f Dockerfile . 2>&1 | tail -3 \
  && docker push registry.digitalocean.com/ctrlsignalregistryimg/placecontext:latest 2>&1 | tail -3 \
  && ssh -i ~/.ssh/id_ed25519 root@100.81.205.22 'set -e
kubectl -n placecontext rollout restart deployment/placecontext
sleep 30
kubectl -n placecontext get pods
# Kill orphaned runs left by the old deployment.
pod="$(kubectl -n placecontext get pod -l app=placecontext-db -o jsonpath="{.items[0].metadata.name}" 2>/dev/null)"
if [ -n "$pod" ]; then
  echo "UPDATE job_runs SET \"Status\" = 'Failed', \"FinishedAt\" = now() WHERE \"Status\" IN ('Queued', 'Running'); UPDATE chain_runs SET \"Status\" = 'Failed', \"FinishedAt\" = now() WHERE \"Status\" IN ('Queued', 'Running');" \
    | kubectl -n placecontext exec "$pod" -- psql -U postgres -d placecontext
fi
' 2>&1
