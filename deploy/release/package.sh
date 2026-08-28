#!/usr/bin/env bash
# Build a source-free PlaceContext release bundle for DigitalOcean Spaces.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="$(git -C "$ROOT" rev-parse --short=8 HEAD)"
ARCH=""
RUNTIME_TAR=""
OUTPUT_DIR="$ROOT/dist/upload"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2#v}"; shift 2 ;;
    --arch) ARCH="$2"; shift 2 ;;
    --runtime-tar) RUNTIME_TAR="$2"; shift 2 ;;
    --output) OUTPUT_DIR="$2"; shift 2 ;;
    -h|--help)
      printf '%s\n' \
        'Build a source-free PlaceContext release bundle.' \
        'Usage: package.sh [--version VERSION] [--arch amd64|arm64]' \
        '                  [--runtime-tar FILE] [--output DIR]'
      exit 0 ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done

if [[ -z "$ARCH" ]]; then
  case "$(uname -m)" in
    x86_64|amd64) ARCH=amd64 ;;
    arm64|aarch64) ARCH=arm64 ;;
    *) printf 'Unsupported architecture: %s\n' "$(uname -m)" >&2; exit 1 ;;
  esac
fi
[[ "$ARCH" == amd64 || "$ARCH" == arm64 ]] || { printf 'Unsupported architecture: %s\n' "$ARCH" >&2; exit 2; }
[[ "$VERSION" =~ ^[A-Za-z0-9._-]+$ ]] || { printf 'Invalid version: %s\n' "$VERSION" >&2; exit 2; }

command -v docker >/dev/null || { printf 'docker is required\n' >&2; exit 1; }
command -v sha256sum >/dev/null || { printf 'sha256sum is required\n' >&2; exit 1; }

TEMP="$(mktemp -d "${TMPDIR:-/tmp}/placecontext-package.XXXXXX")"
cleanup() { rm -rf "$TEMP"; }
trap cleanup EXIT

IMAGE="placecontext:$VERSION"
if [[ -z "$RUNTIME_TAR" ]]; then
  RUNTIME_TAR="$TEMP/placecontext-runtime.tar"
  printf '==> Building %s for linux/%s\n' "$IMAGE" "$ARCH"
  docker buildx build \
    --platform "linux/$ARCH" \
    --tag "$IMAGE" \
    --output "type=docker,dest=$RUNTIME_TAR" \
    "$ROOT"
else
  [[ -f "$RUNTIME_TAR" ]] || { printf 'Runtime archive not found: %s\n' "$RUNTIME_TAR" >&2; exit 1; }
fi

STAGE="$TEMP/placecontext-deploy"
mkdir -p "$STAGE"
cp -R "$ROOT/deploy/release/k3s" "$STAGE/k3s"
cp -R "$ROOT/deploy/release/local-ai" "$STAGE/local-ai"
# Never ship interpreter cache files accidentally left by local validation.
rm -rf "$STAGE/local-ai/__pycache__"
cp "$ROOT/deploy/release/install.sh" "$STAGE/install.sh"
cp "$ROOT/deploy/release/install-production.sh" "$STAGE/install-production.sh"
cp "$ROOT/LICENSE" "$STAGE/LICENSE"
cp "$RUNTIME_TAR" "$STAGE/placecontext-runtime.tar"
printf '%s\n' "$VERSION" > "$STAGE/VERSION"
chmod 0755 "$STAGE/install.sh"
chmod 0755 "$STAGE/install-production.sh"

sed -i \
  "s|__PLACECONTEXT_RUNTIME_IMAGE__|$IMAGE|g" \
  "$STAGE/local-ai/config.yaml" \
  "$STAGE/local-ai/runtime.yaml"
if rg -n '__PLACECONTEXT_RUNTIME_IMAGE__' "$STAGE" >/dev/null; then
  printf 'Runtime image placeholder was not resolved\n' >&2
  exit 1
fi

RELEASE_DIR="$OUTPUT_DIR/releases/$VERSION"
ASSET="placecontext-deploy-$ARCH.tar.gz"
mkdir -p "$RELEASE_DIR" "$OUTPUT_DIR/latest"
tar -C "$TEMP" -czf "$RELEASE_DIR/$ASSET" placecontext-deploy
(
  cd "$RELEASE_DIR"
  sha256sum placecontext-deploy-*.tar.gz > SHA256SUMS
)
cp "$ROOT/deploy/release/install.sh" "$OUTPUT_DIR/install.sh"
cp "$ROOT/join.sh" "$OUTPUT_DIR/join.sh"
printf '%s\n' "$VERSION" > "$RELEASE_DIR/VERSION"
printf '%s\n' "$VERSION" > "$OUTPUT_DIR/latest/VERSION"

printf '==> Built %s\n' "$RELEASE_DIR/$ASSET"
printf '    Version: %s\n' "$VERSION"
printf '    Runtime: %s\n' "$IMAGE"
