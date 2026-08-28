#!/usr/bin/env bash
# Deploy PlaceContext to an existing HA k3s cluster. Cluster creation and stateful services stay operator-owned.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE=""
HOSTNAME=""
TLS_SECRET=""
WAIT=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="$2"; shift 2 ;;
    --hostname) HOSTNAME="$2"; shift 2 ;;
    --tls-secret) TLS_SECRET="$2"; shift 2 ;;
    --no-wait) WAIT=0; shift ;;
    -h|--help)
      printf '%s\n' \
        'Usage: install-production.sh --image REGISTRY/IMAGE@sha256:DIGEST --hostname HOST --tls-secret SECRET' \
        '' \
        'Requires an existing three-server HA k3s cluster, at least one worker, external PostgreSQL,' \
        'off-cluster S3, TLS, and the secrets documented in README.md.'
      exit 0 ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done

die() { printf 'error: %s\n' "$*" >&2; exit 1; }
command -v kubectl >/dev/null || die 'kubectl is required'
[[ "$IMAGE" =~ @sha256:[a-f0-9]{64}$ ]] || die '--image must be pinned by sha256 digest'
[[ "$HOSTNAME" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || die 'invalid --hostname'
[[ ${#HOSTNAME} -le 253 && "$HOSTNAME" != *..* ]] || die 'invalid --hostname'
[[ "$TLS_SECRET" =~ ^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$ ]] || die 'invalid --tls-secret'
[[ -f "$ROOT/k3s/placecontext.yaml" ]] || die 'run this script from a release bundle'

total="$(kubectl get nodes --no-headers | wc -l | tr -d ' ')"
servers="$(kubectl get nodes -l node-role.kubernetes.io/control-plane --no-headers | wc -l | tr -d ' ')"
ready="$(kubectl get nodes --no-headers | awk '$2 ~ /^Ready/ { n++ } END { print n+0 }')"
(( servers >= 3 )) || die "production requires at least 3 control-plane nodes (found $servers)"
(( total > servers )) || die 'production requires at least one worker node'
(( ready == total )) || die "all nodes must be Ready ($ready/$total ready)"

kubectl apply -f "$ROOT/k3s/namespaces.yaml" >/dev/null
for secret in placecontext-db placecontext-portal placecontext-oauth placecontext-dp placecontext-ca placecontext-object-store; do
  kubectl -n placecontext get secret "$secret" >/dev/null 2>&1 || die "missing secret placecontext/$secret"
done
kubectl -n placecontext get secret "$TLS_SECRET" >/dev/null 2>&1 || die "missing TLS secret placecontext/$TLS_SECRET"
secret_value() {
  kubectl -n placecontext get secret "$1" -o "go-template={{with index .data \"$2\"}}{{. | base64decode}}{{end}}"
}
for item in \
  placecontext-db:connection-string placecontext-portal:signing-key placecontext-oauth:key.pem \
  placecontext-dp:key placecontext-ca:tls.crt placecontext-ca:tls.key \
  placecontext-object-store:endpoint placecontext-object-store:ACCESS_KEY_ID \
  placecontext-object-store:ACCESS_SECRET_KEY placecontext-object-store:region \
  placecontext-object-store:force-path-style placecontext-object-store:reports-bucket \
  placecontext-object-store:deps-bucket "$TLS_SECRET:tls.crt" "$TLS_SECRET:tls.key"; do
  secret="${item%%:*}"
  key="${item#*:}"
  [[ -n "$(secret_value "$secret" "$key")" ]] || die "$secret/$key is empty"
done
[[ "$(secret_value placecontext-object-store endpoint)" == https://* ]] \
  || die 'placecontext-object-store/endpoint must use HTTPS'
[[ "$(secret_value placecontext-object-store force-path-style)" =~ ^(true|false)$ ]] \
  || die 'placecontext-object-store/force-path-style must be true or false'

kubectl apply -f "$ROOT/k3s/network-policies.yaml" >/dev/null
sed -e "s|__IMAGE__|$IMAGE|g" -e 's|__IMAGE_PULL_POLICY__|IfNotPresent|g' \
  -e 's|__CLUSTER_ENDPOINT__||g' \
  "$ROOT/k3s/placecontext.yaml" | kubectl apply -f - >/dev/null
sed -e "s|__HOSTNAME__|$HOSTNAME|g" -e "s|__TLS_SECRET__|$TLS_SECRET|g" \
  "$ROOT/k3s/production-ingress.yaml" | kubectl apply -f - >/dev/null
kubectl -n placecontext set env deployment/placecontext \
  PlaceContext__PublicBaseUrl="https://$HOSTNAME" \
  PLACECONTEXT_HOSTNAME="$HOSTNAME" \
  PlaceContext__WorkloadRunner__RequireWorkerNodes=true >/dev/null

kubectl auth can-i create jobs.batch --as=system:serviceaccount:placecontext:placecontext \
  -n placecontext-jobs | grep -qx yes || die 'workload RBAC verification failed'
if [[ "$WAIT" == 1 ]]; then
  kubectl -n placecontext rollout status deployment/placecontext --timeout=10m
fi
printf 'PlaceContext is available at https://%s\n' "$HOSTNAME"
