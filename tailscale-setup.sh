#!/bin/bash
set -euo pipefail

echo "=== Tailscale macOS Installer ==="

if command -v tailscale &>/dev/null; then
    echo "Tailscale is already installed."
else
    echo "Downloading Tailscale..."
    if command -v brew &>/dev/null; then
        brew install --cask tailscale
    else
        TAILSCALE_URL="https://pkgs.tailscale.com/stable/Tailscale-latest.pkg"
        curl -fsSL "$TAILSCALE_URL" -o /tmp/Tailscale.pkg
        sudo installer -pkg /tmp/Tailscale.pkg -target /
        rm -f /tmp/Tailscale.pkg
    fi
    echo "Tailscale installed."
fi

echo "Starting Tailscale..."
sudo tailscaled install-system-daemon 2>/dev/null || true
tailscale up

echo "Flushing DNS cache..."
sudo dscacheutil -flushcache
sudo killall -HUP mDNSResponder

echo "Done. Status:"
tailscale status
