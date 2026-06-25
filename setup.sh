#!/usr/bin/env bash
#
# setup.sh — self-contained, idempotent installer for PlaceContext.
#
# Installs/sets up every prerequisite needed to run PlaceContext locally, then leaves you ready to
# launch with ./run.sh:
#   • .NET 10 SDK            (installed to ~/.dotnet via the official script if not already present)
#   • Docker                 (checked; used for PostgreSQL + the job workload runner)
#   • PostgreSQL 16 + pgvector (pulled + started as the `placecontext-db` container on port 5433)
#   • dotnet-ef local tool   (restored from the tool manifest)
#   • EF Core migrations     (applied to the database)
#   • (optional) Ollama + a small Gemma model for the local LLM provider  [--with-ollama]
#
# Usage:
#   ./setup.sh                 # core setup
#   ./setup.sh --with-ollama   # also install Ollama + pull the local Gemma model
#
# Safe to re-run: every step checks before acting.
set -euo pipefail
cd "$(dirname "$0")"

DOTNET_CHANNEL="10.0"
DB_CONTAINER="placecontext-db"
DB_PORT="5433"
OLLAMA_MODEL="gemma3:4b"
WITH_OLLAMA=0

for arg in "$@"; do
  case "$arg" in
    --with-ollama) WITH_OLLAMA=1 ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $arg (try --help)" >&2; exit 2 ;;
  esac
done

say()  { printf '\n\033[1;36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mWARN:\033[0m %s\n' "$*" >&2; }
have() { command -v "$1" >/dev/null 2>&1; }

# ── .NET 10 SDK ────────────────────────────────────────────────────────────────────────────────
say "Checking for the .NET ${DOTNET_CHANNEL} SDK…"
if have dotnet && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
  echo "    .NET ${DOTNET_CHANNEL} SDK present: $(dotnet --version)"
else
  say "Installing the .NET ${DOTNET_CHANNEL} SDK to \$HOME/.dotnet (official install script)…"
  tmp="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp"
  bash "$tmp" --channel "${DOTNET_CHANNEL}" --install-dir "$HOME/.dotnet"
  rm -f "$tmp"
  export PATH="$HOME/.dotnet:$PATH"
  if ! grep -qs 'HOME/.dotnet' "$HOME/.profile" 2>/dev/null; then
    echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$HOME/.profile"
    warn "Added ~/.dotnet to PATH in ~/.profile — open a new shell or 'source ~/.profile' later."
  fi
fi

# ── Docker ─────────────────────────────────────────────────────────────────────────────────────
say "Checking for Docker…"
if have docker && docker info >/dev/null 2>&1; then
  echo "    Docker is available."
else
  warn "Docker is not available. It is required for PostgreSQL and the job workload runner."
  warn "Install Docker Engine (https://docs.docker.com/engine/install/) and ensure your user can run it, then re-run."
fi

# ── PostgreSQL (dev container) ─────────────────────────────────────────────────────────────────
if have docker && docker info >/dev/null 2>&1; then
  say "Ensuring the ${DB_CONTAINER} PostgreSQL container is running on port ${DB_PORT}…"
  if [ -n "$(docker ps -q -f "name=^${DB_CONTAINER}$")" ]; then
    echo "    ${DB_CONTAINER} already running."
  elif [ -n "$(docker ps -aq -f "name=^${DB_CONTAINER}$")" ]; then
    docker start "${DB_CONTAINER}" >/dev/null && echo "    Started existing ${DB_CONTAINER}."
  else
    docker run -d --name "${DB_CONTAINER}" \
      -e POSTGRES_PASSWORD=postgres -e POSTGRES_USER=postgres -e POSTGRES_DB=placecontext \
      -p "${DB_PORT}:5432" pgvector/pgvector:pg16 >/dev/null
    echo "    Created ${DB_CONTAINER}."
  fi
  printf '    Waiting for PostgreSQL'
  until docker exec "${DB_CONTAINER}" pg_isready -U postgres -d placecontext >/dev/null 2>&1; do
    printf '.'; sleep 1
  done
  echo ' ready.'
else
  warn "Skipping PostgreSQL container (Docker unavailable). Provide a Postgres reachable on localhost:${DB_PORT}."
fi

# ── Restore, build, tools, migrations ──────────────────────────────────────────────────────────
say "Restoring and building the solution…"
dotnet restore
dotnet build --no-restore -clp:ErrorsOnly

say "Restoring local dotnet tools (dotnet-ef)…"
dotnet tool restore

say "Applying EF Core migrations…"
dotnet ef database update \
  --project src/PlaceContext.Infrastructure \
  --startup-project src/PlaceContext.Host

# ── Optional: Ollama + local Gemma model ───────────────────────────────────────────────────────
if [ "$WITH_OLLAMA" -eq 1 ]; then
  say "Setting up Ollama + the local model (${OLLAMA_MODEL})…"
  if ! have ollama; then
    echo "    Installing Ollama (official script)…"
    curl -fsSL https://ollama.com/install.sh | sh
  fi
  if have ollama; then
    echo "    Pulling ${OLLAMA_MODEL} (small enough for ~16GB RAM)…"
    ollama pull "${OLLAMA_MODEL}" || warn "Could not pull ${OLLAMA_MODEL} — pull it manually with: ollama pull ${OLLAMA_MODEL}"
    warn "To use the local model, set PlaceContext:Llm:Provider=ollama in appsettings (Ollama model: ${OLLAMA_MODEL})."
  else
    warn "Ollama install did not complete — see https://ollama.com/download"
  fi
fi

# ── Done ───────────────────────────────────────────────────────────────────────────────────────
say "Setup complete."
cat <<'EOF'

Next steps:
  • Start the app:        ./run.sh        → portal http://localhost:7700, MCP at /mcp
  • Configure the LLM:    edit src/PlaceContext.Host/appsettings.json → PlaceContext:Llm
                          (Provider = none | anthropic | ollama)
  • Run the tests:        dotnet test

EOF
