#!/usr/bin/env bash
#
# Start an already-prepared PlaceContext checkout.
#
# First time here? Run ./run.sh instead; it installs prerequisites, starts PostgreSQL,
# builds the solution, applies migrations, and then launches this same app.
#
# Usage:
#   ./start.sh                    # build and start at http://localhost:7700
#   ./start.sh --port 7710        # use another port
#   ./start.sh --no-build         # start the last build immediately
#   ./start.sh --production       # use production environment settings

set -euo pipefail

# Ensure the .NET SDK and tools (dotnet-ef) are on PATH
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

cd "$(dirname "$0")"

app_port="${PORT:-7700}"
app_environment="${ASPNETCORE_ENVIRONMENT:-Development}"
skip_build=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --port)
      [ "$#" -ge 2 ] || { echo "ERROR: --port requires a value." >&2; exit 2; }
      app_port="$2"
      shift 2
      ;;
    --no-build)
      skip_build=true
      shift
      ;;
    --production)
      app_environment="Production"
      shift
      ;;
    -h|--help)
      sed -n '2,13s/^# \{0,1\}//p' "$0"
      exit 0
      ;;
    *)
      echo "ERROR: unknown option '$1' (try --help)." >&2
      exit 2
      ;;
  esac
done

if ! [[ "$app_port" =~ ^[0-9]+$ ]] || [ "$app_port" -lt 1 ] || [ "$app_port" -gt 65535 ]; then
  echo "ERROR: port must be a number from 1 to 65535." >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: the .NET SDK is not available. Run ./run.sh for the full setup." >&2
  exit 1
fi

if ss -tln 2>/dev/null | awk '{print $4}' | grep -qE "[:.]${app_port}\$"; then
  echo "ERROR: port ${app_port} is already in use." >&2
  echo "Choose another with: ./start.sh --port 7710" >&2
  exit 1
fi

# Wake the standard local database when it already exists. An externally managed Postgres is
# equally valid, so absence of the Docker container is informational rather than fatal.
if command -v docker >/dev/null 2>&1 && docker inspect placecontext-db >/dev/null 2>&1; then
  if ! docker inspect -f '{{.State.Running}}' placecontext-db 2>/dev/null | grep -q true; then
    echo "Starting local PostgreSQL container..."
    docker start placecontext-db >/dev/null
  fi
fi

if [ "$skip_build" = false ]; then
  echo "Building PlaceContext..."
  dotnet build PlaceContext.slnx --nologo -clp:ErrorsOnly
fi

echo
echo "PlaceContext is starting"
echo "  Portal:  http://localhost:${app_port}"
echo "  MCP:     http://localhost:${app_port}/mcp"
echo "  Mode:    ${app_environment}"
echo
echo "A new workspace will open the secure owner-account setup automatically."
echo "Press Ctrl+C to stop."
echo

export ASPNETCORE_URLS="http://0.0.0.0:${app_port}"
export ASPNETCORE_ENVIRONMENT="$app_environment"
exec dotnet run --no-build --no-launch-profile --project src/PlaceContext.Host
