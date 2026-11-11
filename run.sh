#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

if [[ "$(curl --fail --silent --max-time 2 --noproxy '*' http://localhost:7700/healthz)" == '"ok"' ]]; then
  echo "The application is already running."
  exit 0
fi

if [[ -f dist/demo-source/ca-bundle.crt ]]; then
  export SSL_CERT_FILE="${SSL_CERT_FILE:-$PWD/dist/demo-source/ca-bundle.crt}"
  export PGSSLROOTCERT="${PGSSLROOTCERT:-$PWD/dist/demo-source/server.crt}"
fi

dotnet --version >/dev/null
if ! docker info >/dev/null 2>&1; then
  echo "Docker is unavailable. Start Docker and check your access to its daemon." >&2
  exit 1
fi

if [[ -f dist/demo-source/object-store.env && -z "${PlaceContext__ObjectStore__Endpoint:-}" ]]; then
  while IFS='=' read -r key value; do
    case "$key" in
      AWS_ACCESS_KEY_ID) export PlaceContext__ObjectStore__AccessKey="$value" ;;
      AWS_SECRET_ACCESS_KEY) export PlaceContext__ObjectStore__SecretKey="$value" ;;
    esac
  done < dist/demo-source/object-store.env
  export PlaceContext__ObjectStore__Endpoint=http://localhost:8333
  docker start placecontext-artifacts >/dev/null
fi

# Match the local database settings in appsettings.json and preserve the existing development volume.
database=devcontext-validation-postgres
if running=$(docker container inspect --format '{{.State.Running}}' "$database" 2>/dev/null); then
  if [[ "$running" != true ]]; then
    echo "Starting PostgreSQL..."
    docker start "$database" >/dev/null
  fi
else
  echo "Creating local PostgreSQL..."
  docker run --detach --name "$database" \
    --publish 127.0.0.1:5433:5432 \
    --env POSTGRES_DB=placecontext \
    --env POSTGRES_USER=postgres \
    --env POSTGRES_PASSWORD=postgres \
    --volume placecontext-dev-postgres:/var/lib/postgresql/data \
    postgres:17-alpine >/dev/null
fi

echo "Waiting for PostgreSQL at localhost:5433..."
for ((attempt = 0; attempt < 30; attempt++)); do
  if docker exec --env PGPASSWORD=postgres --env PGCONNECT_TIMEOUT=2 "$database" \
    psql --host 127.0.0.1 --username postgres --dbname placecontext \
    --no-psqlrc --set ON_ERROR_STOP=1 --command 'SELECT 1' >/dev/null 2>&1; then
    qemu_image=${PLACECONTEXT_QEMU_IMAGE:-"$PWD/dist/qemu/browser-linux.qcow2"}
    if [[ -r "$qemu_image" && "$qemu_image" == /* ]]; then
      command -v qemu-system-x86_64 >/dev/null || {
        echo "QEMU is unavailable. Install qemu-system-x86_64 or set PlaceContext__Qemu__Enabled=false." >&2
        exit 1
      }
      use_kvm=false
      if [[ -r /dev/kvm && -w /dev/kvm ]]; then use_kvm=true; fi
      export KUBECONFIG=${KUBECONFIG:-"$HOME/.kube/config"}
      export PlaceContext__WorkloadRunner__KubeConfigPath=${PlaceContext__WorkloadRunner__KubeConfigPath:-"$KUBECONFIG"}
      export PlaceContext__Qemu__Enabled=${PlaceContext__Qemu__Enabled:-true}
      export PlaceContext__Qemu__UseKvm=${PlaceContext__Qemu__UseKvm:-$use_kvm}
      export PlaceContext__Qemu__Executor=${PlaceContext__Qemu__Executor:-jev}
      export PlaceContext__Qemu__Agent__Provider=${PlaceContext__Qemu__Agent__Provider:-codex}
      export PlaceContext__Qemu__Agent__Model=${PlaceContext__Qemu__Agent__Model:-gpt-6-astra}
      echo "QEMU browser profile: $qemu_image"
      exec env "PlaceContext__Qemu__Images__browser-linux=$qemu_image" \
        dotnet run --project src/PlaceContext.Host --launch-profile http "$@"
    fi
    echo "PostgreSQL is ready. Starting PlaceContext at http://localhost:7700..."
    exec dotnet run --project src/PlaceContext.Host --launch-profile http "$@"
  fi
  sleep 1
done

echo "PostgreSQL did not become ready after 30 checks. Inspect: docker logs $database" >&2
exit 1
