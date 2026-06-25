#!/usr/bin/env bash
#
# run.sh — start PlaceContext locally.
#
# Brings up the PostgreSQL dependency (via Docker), restores/builds, and runs the
# Host: portal at http://localhost:7700 and MCP over Streamable HTTP at /mcp.
#
set -euo pipefail

cd "$(dirname "$0")"

DB_CONTAINER="placecontext-db"
DB_PORT="5433"

# --- PostgreSQL ------------------------------------------------------------
# Matches PlaceContext:ConnectionString in src/PlaceContext.Host/appsettings.json
if command -v docker >/dev/null 2>&1; then
  if [ -z "$(docker ps -q -f "name=^${DB_CONTAINER}$")" ]; then
    if [ -n "$(docker ps -aq -f "name=^${DB_CONTAINER}$")" ]; then
      echo "Starting existing ${DB_CONTAINER} container..."
      docker start "${DB_CONTAINER}" >/dev/null
    else
      echo "Creating ${DB_CONTAINER} PostgreSQL container on port ${DB_PORT}..."
      docker run -d --name "${DB_CONTAINER}" \
        -e POSTGRES_PASSWORD=postgres \
        -e POSTGRES_USER=postgres \
        -e POSTGRES_DB=placecontext \
        -p "${DB_PORT}:5432" \
        pgvector/pgvector:pg16 >/dev/null
    fi
  else
    echo "${DB_CONTAINER} already running."
  fi

  echo -n "Waiting for PostgreSQL to be ready"
  until docker exec "${DB_CONTAINER}" pg_isready -U postgres -d placecontext >/dev/null 2>&1; do
    echo -n "."
    sleep 1
  done
  echo " ready."
else
  echo "WARNING: docker not found — assuming PostgreSQL is reachable on localhost:${DB_PORT}." >&2
fi

# --- Build & run -----------------------------------------------------------
dotnet restore
dotnet build --no-restore

# --- Database migrations ---------------------------------------------------
# Apply EF Core migrations before launching. The Host also migrates on startup,
# but running it here surfaces schema failures up front and keeps `dotnet ef`
# (a local tool) available for ad-hoc migration work.
echo "Applying database migrations..."
dotnet tool restore
dotnet ef database update \
  --project src/PlaceContext.Infrastructure \
  --startup-project src/PlaceContext.Host

echo "Starting PlaceContext Host → http://localhost:7700 (MCP at /mcp)"
exec dotnet run --no-build --project src/PlaceContext.Host
