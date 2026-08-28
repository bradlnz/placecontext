#!/usr/bin/env bash
# Upload a packaged release to DigitalOcean Spaces, updating latest last.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE="${1:-$ROOT/dist/upload}"
BUCKET="${PLACECONTEXT_SPACES_BUCKET:-placecontext}"
ENDPOINT="${PLACECONTEXT_SPACES_ENDPOINT:-https://syd1.digitaloceanspaces.com}"
VERSION="$(tr -d '[:space:]' < "$SOURCE/latest/VERSION")"
RELEASE_DIR="$SOURCE/releases/$VERSION"

command -v aws >/dev/null || { printf 'aws CLI is required\n' >&2; exit 1; }
[[ -d "$RELEASE_DIR" && -f "$SOURCE/install.sh" && -f "$SOURCE/join.sh" ]] || {
  printf 'Package first; release files are missing under %s\n' "$SOURCE" >&2
  exit 1
}

AWS_ARGS=(--endpoint-url "$ENDPOINT" --region syd1)
DEST="s3://$BUCKET"

printf '==> Uploading PlaceContext %s to %s\n' "$VERSION" "$DEST"
aws s3 cp "$RELEASE_DIR/" "$DEST/releases/$VERSION/" \
  --recursive --acl public-read --cache-control 'public,max-age=31536000,immutable' "${AWS_ARGS[@]}"
aws s3 cp "$SOURCE/install.sh" "$DEST/install.sh" \
  --acl public-read --content-type 'text/x-shellscript' --cache-control 'public,max-age=300' "${AWS_ARGS[@]}"
aws s3 cp "$SOURCE/join.sh" "$DEST/join.sh" \
  --acl public-read --content-type 'text/x-shellscript' --cache-control 'public,max-age=300' "${AWS_ARGS[@]}"
# Publishing the pointer last prevents clients observing a version before its bundle exists.
aws s3 cp "$SOURCE/latest/VERSION" "$DEST/latest/VERSION" \
  --acl public-read --content-type 'text/plain' --cache-control 'no-cache' "${AWS_ARGS[@]}"

printf '==> Published https://%s.syd1.cdn.digitaloceanspaces.com/install.sh\n' "$BUCKET"
