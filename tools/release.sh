#!/usr/bin/env bash
#
# release.sh — build client packages and prepare (or upload) a release.
#
# Usage:
#   ./tools/release.sh                     # package host platform + linux-amd64 if different
#   ./tools/release.sh --all               # linux/darwin × amd64/arm64
#   ./tools/release.sh --linux-only        # linux-amd64 + linux-arm64
#   ./tools/release.sh --no-image          # skip embedding the app image (CLI-only packages)
#   ./tools/release.sh --upload            # after package, upload public-read objects
#   ./tools/release.sh --dry-run --upload  # print upload commands only
#
# Upload (any one works when --upload is set):
#   RELEASE_UPLOAD_CMD   custom command template (see below)
#   or s3cmd / aws / rclone if available
#
# Environment:
#   RELEASE_BUCKET       object-store bucket name (default: placecontext)
#   RELEASE_ENDPOINT     S3-compatible endpoint host (optional; for s3cmd/aws)
#   RELEASE_PREFIX       key prefix inside the bucket (default: empty)
#   RELEASE_PUBLIC_BASE  public base URL printed for install (default: https://get.placecontext.ai)
#   RELEASE_VERSION      override git version stamp
#   PCTL_IMAGE_TAR       path to linux image tarball (default: deploy/placecontext-local.tar)
#
# RELEASE_UPLOAD_CMD examples (use {src} and {dest} placeholders):
#   s3cmd put --acl-public {src} s3://placecontext/{dest}
#   aws s3 cp {src} s3://placecontext/{dest} --acl public-read --endpoint-url https://…
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PCTL="${ROOT}/tools/pctl"
[ -x "$PCTL" ] || { echo "missing tools/pctl" >&2; exit 1; }

ALL=0
LINUX_ONLY=0
NO_IMAGE=0
UPLOAD=0
DRY_RUN=0
TARGETS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --all)        ALL=1; shift;;
    --linux-only) LINUX_ONLY=1; shift;;
    --no-image)   NO_IMAGE=1; shift;;
    --upload)     UPLOAD=1; shift;;
    --dry-run)    DRY_RUN=1; shift;;
    --os)         TARGETS+=("$2/$(uname -m | sed 's/x86_64/amd64/;s/aarch64/arm64/')"); shift 2;;
    --target)     TARGETS+=("$2"); shift 2;;  # e.g. linux/amd64
    -h|--help)
      sed -n '2,35p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "unknown arg: $1" >&2; exit 2;;
  esac
done

if [ -t 1 ]; then
  C_CYAN=$'\033[1;36m'; C_GREEN=$'\033[1;32m'; C_YELLOW=$'\033[1;33m'
  C_BOLD=$'\033[1m'; C_RESET=$'\033[0m'; C_RED=$'\033[1;31m'
else
  C_CYAN=""; C_GREEN=""; C_YELLOW=""; C_BOLD=""; C_RESET=""; C_RED=""
fi
say()  { printf '%s==>%s %s\n' "$C_CYAN" "$C_RESET" "$*"; }
ok()   { printf '%s✓%s %s\n'  "$C_GREEN" "$C_RESET" "$*"; }
warn() { printf '%s!%s %s\n'  "$C_YELLOW" "$C_RESET" "$*" >&2; }
die()  { printf '%s✗%s %s\n'  "$C_RED" "$C_RESET" "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

VERSION="${RELEASE_VERSION:-$(git -C "$ROOT" describe --tags --always 2>/dev/null || date +%Y%m%d)}"
PUBLIC_BASE="${RELEASE_PUBLIC_BASE:-https://get.placecontext.ai}"
BUCKET="${RELEASE_BUCKET:-placecontext}"
PREFIX="${RELEASE_PREFIX:-}"
PREFIX="${PREFIX#/}"; PREFIX="${PREFIX%/}"
[ -n "$PREFIX" ] && PREFIX="${PREFIX}/"

host_os="$(uname -s | tr '[:upper:]' '[:lower:]')"
host_arch="$(uname -m)"
case "$host_arch" in x86_64) host_arch=amd64;; aarch64|arm64) host_arch=arm64;; esac

# ── target matrix ────────────────────────────────────────────────────────────────────────────────
if [ ${#TARGETS[@]} -eq 0 ]; then
  if [ "$ALL" = 1 ]; then
    TARGETS=(linux/amd64 linux/arm64 darwin/amd64 darwin/arm64)
  elif [ "$LINUX_ONLY" = 1 ]; then
    TARGETS=(linux/amd64 linux/arm64)
  else
    TARGETS=("${host_os}/${host_arch}")
    # Always ship a linux-amd64 server package when developing on another host OS.
    if [ "${host_os}/${host_arch}" != "linux/amd64" ]; then
      TARGETS+=(linux/amd64)
    fi
  fi
fi

pkg_args=()
[ "$NO_IMAGE" = 1 ] && pkg_args+=(--no-image)

say "Release ${VERSION}"
printf '  targets: %s\n' "${TARGETS[*]}"
printf '  image:   %s\n' "$([ "$NO_IMAGE" = 1 ] && echo skip || echo include)"

BUILT=()
for t in "${TARGETS[@]}"; do
  os="${t%%/*}"
  arch="${t##*/}"
  case "$os" in linux|darwin) ;; *) die "unsupported os in target $t";; esac
  case "$arch" in amd64|arm64) ;; *) die "unsupported arch in target $t";; esac

  # App image is linux-only; for darwin packages still embed linux image of matching arch when possible.
  say "Packaging ${os}/${arch}…"
  if [ "$os" = darwin ] && [ "$NO_IMAGE" = 0 ]; then
    # Image for k3d on Mac is still a linux container image — package builds linux image for --arch.
    "$PCTL" package --os "$os" --arch "$arch" "${pkg_args[@]+"${pkg_args[@]}"}"
  else
    "$PCTL" package --os "$os" --arch "$arch" "${pkg_args[@]+"${pkg_args[@]}"}"
  fi
  asset="placecontext-${os}-${arch}.tar.gz"
  [ -s "dist/${asset}" ] || die "expected dist/${asset}"
  # Ensure latest/ + versioned copies exist (cmd_package already does this; re-stamp VERSION).
  mkdir -p "dist/latest" "dist/${VERSION}"
  cp -f "dist/${asset}" "dist/latest/${asset}"
  cp -f "dist/${asset}" "dist/${VERSION}/${asset}"
  printf '%s\n' "$VERSION" > dist/latest/VERSION
  printf '%s\n' "$VERSION" > "dist/${VERSION}/VERSION"
  cp -f deploy/install.sh dist/install.sh
  chmod +x dist/install.sh
  BUILT+=("$asset")
  ok "$asset ($(du -h "dist/${asset}" | cut -f1))"
done

# Manifest of what to upload
MANIFEST=()
MANIFEST+=("install.sh|dist/install.sh")
MANIFEST+=("latest/VERSION|dist/latest/VERSION")
MANIFEST+=("${VERSION}/VERSION|dist/${VERSION}/VERSION")
for asset in "${BUILT[@]}"; do
  MANIFEST+=("latest/${asset}|dist/latest/${asset}")
  MANIFEST+=("${VERSION}/${asset}|dist/${VERSION}/${asset}")
done

# Clean upload staging dir (copy only release objects — no old tarballs)
STAGE="dist/upload"
rm -rf "$STAGE"
mkdir -p "$STAGE/latest" "$STAGE/${VERSION}"
cp -f dist/install.sh "$STAGE/install.sh"
cp -f dist/latest/VERSION "$STAGE/latest/VERSION"
cp -f "dist/${VERSION}/VERSION" "$STAGE/${VERSION}/VERSION"
for asset in "${BUILT[@]}"; do
  cp -f "dist/latest/${asset}" "$STAGE/latest/${asset}"
  cp -f "dist/${VERSION}/${asset}" "$STAGE/${VERSION}/${asset}"
done

# Human-readable checklist
{
  echo "PlaceContext release ${VERSION}"
  echo ""
  echo "Upload every file under this directory preserving paths (public-read):"
  echo ""
  echo "  install.sh"
  echo "  latest/VERSION"
  for asset in "${BUILT[@]}"; do echo "  latest/${asset}"; done
  echo "  ${VERSION}/VERSION"
  for asset in "${BUILT[@]}"; do echo "  ${VERSION}/${asset}"; done
  echo ""
  echo "Client install after publish:"
  echo "  curl -fsSL ${PUBLIC_BASE}/install.sh | bash"
  echo ""
  echo "Built: $(date -u +%Y-%m-%dT%H:%MZ) on ${host_os}/${host_arch}"
} > "$STAGE/UPLOAD.txt"

say "Staging complete → dist/upload/"
cat "$STAGE/UPLOAD.txt"

if [ "$UPLOAD" != 1 ]; then
  printf '\n%sNext:%s upload dist/upload/ (or re-run with --upload)\n' "$C_BOLD" "$C_RESET"
  exit 0
fi

# ── upload ───────────────────────────────────────────────────────────────────────────────────────
upload_one() {
  local dest="$1" src="$2"
  local key="${PREFIX}${dest}"
  if [ -n "${RELEASE_UPLOAD_CMD:-}" ]; then
    local cmd="${RELEASE_UPLOAD_CMD//\{src\}/$src}"
    cmd="${cmd//\{dest\}/$key}"
    if [ "$DRY_RUN" = 1 ]; then
      printf '  %s\n' "$cmd"
      return 0
    fi
    # shellcheck disable=SC2086
    eval "$cmd" || die "upload failed: $key"
    return 0
  fi

  if have s3cmd; then
    local args=(put --acl-public "$src" "s3://${BUCKET}/${key}")
    [ -n "${RELEASE_ENDPOINT:-}" ] && args=(--host="$RELEASE_ENDPOINT" --host-bucket="%(bucket)s.${RELEASE_ENDPOINT}" "${args[@]}")
    if [ "$DRY_RUN" = 1 ]; then
      printf '  s3cmd %s\n' "${args[*]}"
      return 0
    fi
    s3cmd "${args[@]}" || die "s3cmd upload failed: $key"
    return 0
  fi

  if have aws; then
    local args=(s3 cp "$src" "s3://${BUCKET}/${key}" --acl public-read)
    [ -n "${RELEASE_ENDPOINT:-}" ] && args+=(--endpoint-url "https://${RELEASE_ENDPOINT}")
    if [ "$DRY_RUN" = 1 ]; then
      printf '  aws %s\n' "${args[*]}"
      return 0
    fi
    aws "${args[@]}" || die "aws upload failed: $key"
    return 0
  fi

  if have rclone; then
    local remote="${RELEASE_RCLONE_REMOTE:-spaces:${BUCKET}}"
    if [ "$DRY_RUN" = 1 ]; then
      printf '  rclone copyto %s %s/%s\n' "$src" "$remote" "$key"
      return 0
    fi
    rclone copyto "$src" "${remote}/${key}" || die "rclone upload failed: $key"
    return 0
  fi

  die "no uploader found — install s3cmd, aws, or rclone, or set RELEASE_UPLOAD_CMD='… {src} … {dest}'"
}

say "Uploading release ${VERSION}…"
for entry in "${MANIFEST[@]}"; do
  dest="${entry%%|*}"
  src="${entry#*|}"
  [ -f "$src" ] || die "missing $src"
  printf '  %s → %s\n' "$src" "$dest"
  upload_one "$dest" "$src"
done
ok "Upload finished."
printf '\nInstall:\n  curl -fsSL %s/install.sh | bash\n' "$PUBLIC_BASE"
