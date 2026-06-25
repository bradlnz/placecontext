#!/usr/bin/env bash
#
# selfhost.sh — DEPRECATED shim. The self-host / fleet flow now lives in `pctl`.
#
#   Old                                            New
#   ─────────────────────────────────────────────  ─────────────────────────────────────────────
#   selfhost.sh --activation-key KEY [--image IMG]  pctl server up --activation-key KEY [--image IMG]
#   selfhost.sh --agent --server-url U --node-token T   pctl agent join --server-url U --node-token T
#
# This shim translates the old flags and forwards to pctl so existing docs keep working.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"

args=(); mode="server"
while [ $# -gt 0 ]; do
  case "$1" in
    --agent) mode="agent"; shift ;;
    *) args+=("$1"); shift ;;
  esac
done

echo "selfhost.sh is deprecated — forwarding to: pctl ${mode} ..." >&2
if [ "$mode" = "agent" ]; then
  exec "$HERE/pctl" agent join "${args[@]}"
else
  exec "$HERE/pctl" server up "${args[@]}"
fi
