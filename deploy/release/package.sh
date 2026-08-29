#!/usr/bin/env bash
# Build source-free PlaceContext GitHub release assets.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="$(git -C "$ROOT" rev-parse --short=8 HEAD)"
IMAGE=""
OUTPUT_DIR="$ROOT/dist/upload"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2#v}"; shift 2 ;;
    --image) IMAGE="$2"; shift 2 ;;
    --output) OUTPUT_DIR="$2"; shift 2 ;;
    -h|--help)
      printf '%s\n' \
        'Build a source-free PlaceContext release bundle.' \
        'Usage: package.sh [--version VERSION] --image IMAGE [--output DIR]'
      exit 0 ;;
    *) printf 'Unknown option: %s\n' "$1" >&2; exit 2 ;;
  esac
done

[[ "$VERSION" =~ ^[A-Za-z0-9._-]+$ ]] || { printf 'Invalid version: %s\n' "$VERSION" >&2; exit 2; }
[[ "$IMAGE" =~ ^ghcr\.io/[a-z0-9._/-]+(@sha256:[a-f0-9]{64}|:[A-Za-z0-9._-]+)$ ]] \
  || { printf 'Invalid or missing GHCR image: %s\n' "$IMAGE" >&2; exit 2; }

command -v sha256sum >/dev/null || { printf 'sha256sum is required\n' >&2; exit 1; }

TEMP="$(mktemp -d "${TMPDIR:-/tmp}/placecontext-package.XXXXXX")"
cleanup() { rm -rf "$TEMP"; }
trap cleanup EXIT

STAGE="$TEMP/placecontext-deploy"
mkdir -p "$STAGE"
cp -R "$ROOT/deploy/release/k3s" "$STAGE/k3s"
cp -R "$ROOT/deploy/release/local-ai" "$STAGE/local-ai"
# Never ship interpreter cache files accidentally left by local validation.
rm -rf "$STAGE/local-ai/__pycache__"
cp "$ROOT/deploy/release/install.sh" "$STAGE/install.sh"
cp "$ROOT/deploy/release/install-production.sh" "$STAGE/install-production.sh"
cp "$ROOT/LICENSE" "$STAGE/LICENSE"
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
ASSET="placecontext-deploy.tar.gz"
mkdir -p "$RELEASE_DIR" "$OUTPUT_DIR/latest"
tar -C "$TEMP" -czf "$RELEASE_DIR/$ASSET" placecontext-deploy
(
  cd "$RELEASE_DIR"
  sha256sum "$ASSET" > SHA256SUMS
)
cp "$ROOT/deploy/release/install.sh" "$OUTPUT_DIR/install.sh"
cp "$ROOT/join.sh" "$OUTPUT_DIR/join.sh"
printf '%s\n' "$VERSION" > "$RELEASE_DIR/VERSION"
printf '%s\n' "$VERSION" > "$OUTPUT_DIR/latest/VERSION"

printf '==> Built %s\n' "$RELEASE_DIR/$ASSET"
printf '    Version: %s\n' "$VERSION"
printf '    Runtime: %s\n' "$IMAGE"
