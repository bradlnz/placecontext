#!/usr/bin/env bash
# setup-tls.sh — Post-DNS-propagation script for Let's Encrypt TLS on
# feasibility.ossenpropertygroup.com.au.
#
# Run AFTER updating DNS to point at the public IP (170.64.208.233).
# Verifies DNS, cleans stale cert-manager challenges, forces renewal, watches
# the order complete, and verifies HTTPS end-to-end.
set -euo pipefail

HOST="feasibility.ossenpropertygroup.com.au"
EXPECTED_IP="170.64.208.233"
SECRET_NAME="feasibility-tls"
NAMESPACE="default"
TIMEOUT=300  # 5 minutes for challenge completion

info()  { printf "\033[1;34m▸ %s\033[0m\n" "$*"; }
ok()    { printf "\033[1;32m✔ %s\033[0m\n" "$*"; }
warn()  { printf "\033[1;33m⚠ %s\033[0m\n" "$*"; }
fail()  { printf "\033[1;31m✘ %s\033[0m\n" "$*"; exit 1; }

# ── 1. DNS check ────────────────────────────────────────────────────────────
info "Checking DNS resolution for $HOST …"
RESOLVED=$(dig +short "$HOST" A 2>/dev/null | head -1)
if [ -z "$RESOLVED" ]; then
    fail "DNS not resolving yet — propagate A record pointing to $EXPECTED_IP and re-run."
fi
if [ "$RESOLVED" != "$EXPECTED_IP" ]; then
    fail "DNS resolves to $RESOLVED (expected $EXPECTED_IP). Wait for propagation."
fi
ok "DNS resolves to $RESOLVED ✓"

# ── 2. Clean stale challenges / orders ──────────────────────────────────────
info "Cleaning any stale cert-manager challenge resources …"
kubectl delete challenge --all -n "$NAMESPACE" --ignore-not-found 2>/dev/null || true
kubectl delete order --all -n "$NAMESPACE" --ignore-not-found 2>/dev/null || true

# ── 3. Delete old secret to force fresh issuance ────────────────────────────
info "Deleting TLS secret $SECRET_NAME to force renewal …"
kubectl delete secret "$SECRET_NAME" -n "$NAMESPACE" --ignore-not-found 2>/dev/null || true

# ── 4. Annotate Certificate for force-renewal ──────────────────────────────
info "Annotating certificate for force-renewal …"
CERT_NAME=$(kubectl get certificate -n "$NAMESPACE" -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || echo "")
if [ -z "$CERT_NAME" ]; then
    # cert-manager creates the Certificate from the Ingress; ensure the Ingress exists
    info "No explicit Certificate resource found — cert-manager will create one from the Ingress."
    info "Touching the Ingress to trigger reconciliation …"
    kubectl annotate ingress placecontext -n "$NAMESPACE" \
        cert-manager.io/issue-temporary-certificate="true" \
        --overwrite 2>/dev/null || true
    kubectl annotate ingress placecontext -n "$NAMESPACE" \
        force-renewal="$(date -u +%Y%m%d%H%M%S)" \
        --overwrite 2>/dev/null || true
else
    kubectl annotate certificate "$CERT_NAME" -n "$NAMESPACE" \
        cert-manager.io/issue-temporary-certificate="true" \
        --overwrite 2>/dev/null || true
    kubectl annotate certificate "$CERT_NAME" -n "$NAMESPACE" \
        force-renewal="$(date -u +%Y%m%d%H%M%S)" \
        --overwrite 2>/dev/null || true
    ok "Annotated Certificate $CERT_NAME"
fi

# ── 5. Verify HTTP-01 challenge path is reachable ──────────────────────────
info "Verifying HTTP-01 challenge path is reachable from outside …"
CHALLENGE_OK=$(curl -s -o /dev/null -w "%{http_code}" \
    "http://$HOST/.well-known/acme-challenge/health-check" 2>/dev/null || echo "000")
# Any non-5xx response means Traefik is routing correctly (404 is expected for a dummy path)
if [[ "$CHALLENGE_OK" =~ ^[45] ]]; then
    ok "HTTP path reachable (status $CHALLENGE_OK — Traefik is routing)"
else
    warn "HTTP path returned status $CHALLENGE_OK — Traefik may not be listening on port 80"
fi

# ── 6. Watch certificate readiness ─────────────────────────────────────────
info "Watching certificate issuance (timeout ${TIMEOUT}s) …"
ELAPSED=0
INTERVAL=5
while [ $ELAPSED -lt $TIMEOUT ]; do
    STATUS=$(kubectl get certificate -n "$NAMESPACE" -o jsonpath='{.items[0].status.conditions[?(@.type=="Ready")].status}' 2>/dev/null || echo "")
    if [ "$STATUS" = "True" ]; then
        ok "Certificate is Ready!"
        break
    fi

    # Check for challenges
    CHALLENGES=$(kubectl get challenge -n "$NAMESPACE" --no-headers 2>/dev/null | wc -l)
    ORDERS=$(kubectl get order -n "$NAMESPACE" --no-headers 2>/dev/null | wc -l)
    printf "  [%3ds] certificate not ready yet — %s challenge(s), %s order(s) …\r" \
        "$ELAPSED" "$CHALLENGES" "$ORDERS"
    sleep $INTERVAL
    ELAPSED=$((ELAPSED + INTERVAL))
done

if [ $ELAPSED -ge $TIMEOUT ]; then
    warn "Timed out waiting for certificate. Check cert-manager logs:"
    echo "  kubectl logs -n cert-manager deployment/cert-manager -f"
    echo "  kubectl describe certificate -n $NAMESPACE"
    echo "  kubectl describe challenge -n $NAMESPACE"
    exit 1
fi

# ── 7. Verify HTTPS ─────────────────────────────────────────────────────────
info "Verifying HTTPS on $HOST …"
HTTPS_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "https://$HOST/" 2>/dev/null || echo "000")
if [ "$HTTPS_CODE" = "000" ]; then
    fail "HTTPS connection failed — check Traefik TLS configuration"
fi
ok "HTTPS returned status $HTTPS_CODE"

# ── 8. Verify certificate details ──────────────────────────────────────────
info "Certificate details:"
echo | openssl s_client -servername "$HOST" -connect "$HOST:443" 2>/dev/null \
    | openssl x509 -noout -subject -issuer -dates 2>/dev/null || true

echo ""
ok "TLS setup complete for $HOST"
