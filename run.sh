#!/usr/bin/env bash
#
# run.sh — set up and start PlaceContext locally with one command.
#
# Runs ./setup.sh (idempotent: .NET 10 SDK, Docker check, PostgreSQL+pgvector
# container, restore/build, dotnet-ef, migrations), then launches the Host:
# portal at http://localhost:${PORT} and MCP over Streamable HTTP at /mcp.
#
# Usage:
#   ./run.sh                 # full setup + run on port 7700
#   PORT=7710 ./run.sh       # run on a different port (e.g. when the k3d
#                            # dev cluster's load balancer holds 7700)
#
set -euo pipefail

cd "$(dirname "$0")"

PORT="${PORT:-7700}"

./setup.sh "$@"

# setup.sh may have installed the SDK to ~/.dotnet in its own shell; make sure
# this shell can see it too.
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi

if ss -tln 2>/dev/null | awk '{print $4}' | grep -qE "[:.]${PORT}\$"; then
  echo "ERROR: port ${PORT} is already in use (on this machine that is usually the" >&2
  echo "k3d dev cluster's load balancer — check with: docker ps | grep ${PORT})." >&2
  echo "Re-run on a free port, e.g.: PORT=7710 ./run.sh" >&2
  exit 1
fi

# --no-launch-profile: launchSettings.json would otherwise override the bind
# URL to localhost:5169 via ASPNETCORE_URLS. That also skips the profile's
# ASPNETCORE_ENVIRONMENT=Development, so set it here (overridable).
echo "Starting PlaceContext Host → http://localhost:${PORT} (MCP at /mcp)"
export ASPNETCORE_URLS="http://localhost:${PORT}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
exec dotnet run --no-build --no-launch-profile --project src/PlaceContext.Host
