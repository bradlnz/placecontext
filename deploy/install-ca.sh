#!/bin/bash
set -euo pipefail

CERT_PATH="${1:?Usage: install-ca.sh <path-to-ca.crt>}"
CA_NAME="PlaceContext Internal CA"

if [ ! -f "$CERT_PATH" ]; then
  echo "Error: $CERT_PATH not found" >&2
  exit 1
fi

TMP_CA=$(mktemp /tmp/placecontext-ca-XXXXXX.crt)
cp "$CERT_PATH" "$TMP_CA"

install_system() {
  if command -v update-ca-trust &>/dev/null; then
    # Arch / Fedora
    sudo mkdir -p /etc/ca-certificates/trust-anchors
    sudo cp "$TMP_CA" "/etc/ca-certificates/trust-anchors/placecontext-internal-ca.crt"
    sudo update-ca-trust extract
    echo "Installed to system trust store (update-ca-trust)"
  elif command -v update-ca-certificates &>/dev/null; then
    # Ubuntu / Debian
    sudo cp "$TMP_CA" /usr/local/share/ca-certificates/placecontext-internal-ca.crt
    sudo update-ca-certificates
    echo "Installed to system trust store (update-ca-certificates)"
  else
    echo "Warning: no system CA update command found — skipping system trust store"
  fi
}

install_chrome() {
  NSS_DB="$HOME/.pki/nssdb"
  if command -v certutil &>/dev/null; then
    mkdir -p "$NSS_DB"
    # Initialize DB if empty
    if [ ! -f "$NSS_DB/cert9.db" ]; then
      certutil -d sql:"$NSS_DB" -N --empty-password
    fi
    certutil -d sql:"$NSS_DB" -A -t "C,," -n "$CA_NAME" -i "$TMP_CA" 2>/dev/null \
      && echo "Installed to Chrome/NSS trust store" \
      || echo "Warning: NSS import failed (cert may already exist)"
  else
    echo "Warning: certutil not found — install nss package for Chrome trust"
  fi
}

install_macos() {
  sudo security add-trusted-cert -d -r trustRoot \
    -k /Library/Keychains/System.keychain "$TMP_CA"
  echo "Installed to macOS system keychain"
}

case "$(uname -s)" in
  Linux*)  install_system; install_chrome ;;
  Darwin*) install_macos ;;
  *)       echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
esac

rm -f "$TMP_CA"
echo "Done. Restart your browser."
