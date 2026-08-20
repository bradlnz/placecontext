#!/usr/bin/env bash
#
# PlaceContext — curl-friendly installer.
#
# Installs the global `placecontext` command, then (on a TTY) launches the TUI.
# Cluster install / upgrade / connect are done from the TUI after this step.
#
#   curl -fsSL https://get.placecontext.ai/install.sh | bash
#
# Options (pass after bash -s -- …):
#   --version X.Y.Z    install a specific release (default: latest)
#   --dir DIR          install root (default: ~/.placecontext)
#   --bin DIR          PATH directory for the shim (default: ~/.local/bin)
#   --base URL         download base (advanced / mirrors)
#   --no-launch        do not open the TUI at the end
#   --help
#
# Environment:
#   PLACECONTEXT_BASE_URL     download base (default below)
#   PLACECONTEXT_VERSION      same as --version
#   PLACECONTEXT_HOME         same as --dir
#
set -euo pipefail

# Internal download base — not printed to the user.
BASE_URL="${PLACECONTEXT_BASE_URL:-${PLACECONTEXT_BUCKET_URL:-https://placecontext.syd1.cdn.digitaloceanspaces.com}}"
VERSION="${PLACECONTEXT_VERSION:-}"
INSTALL_ROOT="${PLACECONTEXT_HOME:-$HOME/.placecontext}"
BIN_DIR="${PLACECONTEXT_BIN_DIR:-$HOME/.local/bin}"
LAUNCH=1

while [ $# -gt 0 ]; do
  case "$1" in
    --version)   VERSION="$2"; shift 2;;
    --dir)       INSTALL_ROOT="$2"; shift 2;;
    --bin)       BIN_DIR="$2"; shift 2;;
    --base|--bucket) BASE_URL="$2"; shift 2;;
    --no-launch) LAUNCH=0; shift;;
    -h|--help)
      sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "Unknown arg: $1" >&2; exit 2;;
  esac
done

BASE_URL="${BASE_URL%/}"

OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
ARCH="$(uname -m)"
case "$ARCH" in x86_64|amd64) ARCH=amd64;; aarch64|arm64) ARCH=arm64;; *)
  echo "Unsupported architecture: $ARCH" >&2; exit 1;;
esac
case "$OS" in linux|darwin) ;; *)
  echo "Unsupported OS: $OS (need linux or darwin)" >&2; exit 1;;
esac

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

have curl || die "curl is required"

extract_zip() {
  local archive="$1" destination="$2"
  if have unzip; then
    unzip -q "$archive" -d "$destination"
  elif have bsdtar; then
    bsdtar -xf "$archive" -C "$destination"
  elif have ditto; then
    ditto -x -k "$archive" "$destination"
  elif have python3; then
    python3 -m zipfile -e "$archive" "$destination"
  else
    die "unzip, bsdtar, ditto, or python3 is required to unpack PlaceContext"
  fi
}

fetch() {
  # $1=url $2=dest — fail clearly on 404
  local url="$1" dest="$2"
  if ! curl -fsSL --retry 3 --retry-delay 1 -o "$dest" "$url"; then
    return 1
  fi
  return 0
}

CHANNEL="latest"
if [ -n "$VERSION" ]; then
  CHANNEL="${VERSION#v}"
  VERSION="${VERSION#v}"
else
  say "Checking for latest version…"
  TMPV="$(mktemp)"
  if fetch "${BASE_URL}/latest/VERSION" "$TMPV"; then
    VERSION="$(tr -d '[:space:]' < "$TMPV")"
    CHANNEL="latest"
  fi
  rm -f "$TMPV"
  [ -n "${VERSION:-}" ] || die "could not determine latest version — try again later."
fi
ok "version ${VERSION}"

ASSET="placecontext-${OS}-${ARCH}.zip"
URLS=(
  "${BASE_URL}/${CHANNEL}/${ASSET}"
  "${BASE_URL}/${VERSION}/${ASSET}"
  "${BASE_URL}/v${VERSION}/${ASSET}"
  "${BASE_URL}/releases/${VERSION}/${ASSET}"
)

TMP="$(mktemp -d "${TMPDIR:-/tmp}/placecontext-install.XXXXXX")"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

say "Downloading PlaceContext…"
ARCHIVE=""
for url in "${URLS[@]}"; do
  if fetch "$url" "$TMP/pkg.zip"; then
    ARCHIVE="$url"
    ok "download complete"
    break
  fi
done
[ -n "$ARCHIVE" ] || die "download failed — check your network and try again."

say "Installing → ${INSTALL_ROOT}…"
mkdir -p "$INSTALL_ROOT"
rm -rf "$INSTALL_ROOT/bin" "$INSTALL_ROOT/lib"
mkdir -p "$INSTALL_ROOT/bin" "$INSTALL_ROOT/lib"
extract_zip "$TMP/pkg.zip" "$TMP"

# Locate stage root (flat or single top-level dir).
STAGE="$TMP"
if [ -x "$TMP/placecontext" ] || [ -d "$TMP/lib" ]; then
  STAGE="$TMP"
else
  top="$(find "$TMP" -mindepth 1 -maxdepth 1 -type d ! -name '.*' | head -1 || true)"
  [ -n "$top" ] && STAGE="$top"
fi

# Binary: prefer top-level placecontext (the Go TUI/CLI).
if [ -x "$STAGE/placecontext" ]; then
  install -m755 "$STAGE/placecontext" "$INSTALL_ROOT/bin/placecontext"
elif [ -x "$STAGE/bin/placecontext" ]; then
  install -m755 "$STAGE/bin/placecontext" "$INSTALL_ROOT/bin/placecontext"
elif [ -x "$STAGE/deploy/tui/placecontext" ]; then
  install -m755 "$STAGE/deploy/tui/placecontext" "$INSTALL_ROOT/bin/placecontext"
elif [ -x "$STAGE/deploy/tui/placecontext-tui" ]; then
  install -m755 "$STAGE/deploy/tui/placecontext-tui" "$INSTALL_ROOT/bin/placecontext"
else
  die "archive has no placecontext binary"
fi

# Assets used by install/upgrade/connect from the TUI.
if [ -d "$STAGE/lib" ]; then
  cp -a "$STAGE/lib/." "$INSTALL_ROOT/lib/"
elif [ -d "$STAGE/deploy" ]; then
  mkdir -p "$INSTALL_ROOT/lib/k3s"
  [ -x "$STAGE/deploy/placecontext" ] && install -m755 "$STAGE/deploy/placecontext" "$INSTALL_ROOT/lib/placecontext-engine"
  [ -d "$STAGE/deploy/k3s" ] && cp -a "$STAGE/deploy/k3s/." "$INSTALL_ROOT/lib/k3s/" 2>/dev/null || true
  [ -f "$STAGE/deploy/placecontext-local.tar" ] && cp -a "$STAGE/deploy/placecontext-local.tar" "$INSTALL_ROOT/lib/" || true
fi
[ -x "$STAGE/placecontext-engine" ] && install -m755 "$STAGE/placecontext-engine" "$INSTALL_ROOT/lib/placecontext-engine"
# Remember download base for upgrades (not shown to the user).
printf '%s\n' "$BASE_URL" > "$INSTALL_ROOT/BASE_URL"
# Back-compat filename for earlier installs.
printf '%s\n' "$BASE_URL" > "$INSTALL_ROOT/BUCKET_URL"
if [ -f "$STAGE/VERSION" ]; then
  cp "$STAGE/VERSION" "$INSTALL_ROOT/VERSION"
else
  printf '%s\n' "$VERSION" > "$INSTALL_ROOT/VERSION"
fi

say "Installing command…"
mkdir -p "$BIN_DIR"
ln -sfn "$INSTALL_ROOT/bin/placecontext" "$BIN_DIR/placecontext"
ok "placecontext installed"

case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    warn "Add ${BIN_DIR} to your PATH, e.g.:"
    printf '    echo '\''export PATH="%s:$PATH"'\'' >> ~/.bashrc && source ~/.bashrc\n' "$BIN_DIR"
    export PATH="$BIN_DIR:$PATH"
    ;;
esac

printf '\n%sPlaceContext ready.%s\n' "$C_BOLD" "$C_RESET"
printf '  Run:  %splacecontext%s\n' "$C_GREEN" "$C_RESET"
printf '  That opens the TUI — install, upgrade, or connect a cluster from there.\n\n'

if [ "$LAUNCH" = 1 ] && [ -t 0 ] && [ -t 1 ]; then
  export PLACECONTEXT_HOME="$INSTALL_ROOT"
  export PLACECONTEXT_BASE_URL="$BASE_URL"
  exec "$BIN_DIR/placecontext"
fi
