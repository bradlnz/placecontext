#!/usr/bin/env bash
#
# activation.sh — licensor tool to mint and verify PlaceContext self-host activation keys.
#
# An activation key is  base64url(payloadJson).base64url(ecdsa-p256-sha256-DER-signature)
# signed with the licensor's private key (tools/activation-signing-key.pem). The deployed app
# verifies it offline against the public key baked into appsettings (PlaceContext:Activation:PublicKey).
#
# Subcommands:
#   keygen                          Generate a new P-256 keypair; prints the public key (base64 SPKI)
#                                   to paste into appsettings. Private key → tools/activation-signing-key.pem
#   pubkey                          Print the current public key (base64 SPKI) for appsettings
#   sign --org NAME [--plan TIER] [--exp YYYY-MM-DD]
#                                   Mint an activation key. Omit --exp for a perpetual key.
#
# Examples:
#   ./tools/activation.sh keygen
#   ./tools/activation.sh sign --org "Acme Inc" --plan pro --exp 2027-01-01
set -euo pipefail
cd "$(dirname "$0")/.."

KEY_FILE="tools/activation-signing-key.pem"
b64url() { base64 -w0 | tr '+/' '-_' | tr -d '='; }

cmd="${1:-}"; shift || true

case "$cmd" in
  keygen)
    openssl ecparam -name prime256v1 -genkey -noout -out "$KEY_FILE"
    chmod 600 "$KEY_FILE"
    echo "Private key written to $KEY_FILE (keep secret — it is gitignored)."
    echo "Public key (paste into appsettings PlaceContext:Activation:PublicKey):"
    openssl ec -in "$KEY_FILE" -pubout -outform DER 2>/dev/null | base64 -w0; echo
    ;;

  pubkey)
    [ -f "$KEY_FILE" ] || { echo "No signing key at $KEY_FILE — run 'keygen' first." >&2; exit 1; }
    openssl ec -in "$KEY_FILE" -pubout -outform DER 2>/dev/null | base64 -w0; echo
    ;;

  sign)
    [ -f "$KEY_FILE" ] || { echo "No signing key at $KEY_FILE — run 'keygen' first." >&2; exit 1; }
    org=""; plan=""; exp=""
    while [ $# -gt 0 ]; do
      case "$1" in
        --org)  org="$2"; shift 2 ;;
        --plan) plan="$2"; shift 2 ;;
        --exp)  exp="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
      esac
    done
    [ -n "$org" ] || { echo "--org is required" >&2; exit 2; }

    # Build a compact JSON payload (no spaces) so the signed bytes match the encoded bytes.
    payload="{\"org\":\"${org}\""
    [ -n "$plan" ] && payload="${payload},\"plan\":\"${plan}\""
    if [ -n "$exp" ]; then
      exp_secs="$(date -u -d "${exp}" +%s)"
      payload="${payload},\"exp\":${exp_secs}"
    fi
    payload="${payload}}"

    p_b64="$(printf '%s' "$payload" | b64url)"
    s_b64="$(printf '%s' "$payload" | openssl dgst -sha256 -sign "$KEY_FILE" | b64url)"
    echo "${p_b64}.${s_b64}"
    ;;

  *)
    grep '^#' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;
esac
