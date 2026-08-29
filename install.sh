#!/usr/bin/env bash
set -euo pipefail

curl -fsSL --retry 3 --retry-delay 2 \
  https://github.com/bradlnz/placecontext/releases/latest/download/install.sh |
  bash -s -- "$@"
