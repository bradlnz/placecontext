#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
image_path=${PLACECONTEXT_QEMU_IMAGE:-"$repo_dir/dist/qemu/browser-linux.qcow2"}
if [[ ! -r "$image_path" || "$image_path" != /* ]]; then
  echo 'Set PLACECONTEXT_QEMU_IMAGE to an absolute path to a prepared desktop qcow2 image.' >&2
  exit 1
fi
command -v qemu-system-x86_64 >/dev/null
use_kvm=false
if [[ -r /dev/kvm && -w /dev/kvm ]]; then use_kvm=true; fi
export KUBECONFIG=${KUBECONFIG:-"$HOME/.kube/config"}
export PlaceContext__WorkloadRunner__KubeConfigPath=${PlaceContext__WorkloadRunner__KubeConfigPath:-"$KUBECONFIG"}
export PlaceContext__Qemu__Enabled=${PlaceContext__Qemu__Enabled:-true}
export PlaceContext__Qemu__UseKvm=${PlaceContext__Qemu__UseKvm:-$use_kvm}
export PlaceContext__Qemu__Executor=${PlaceContext__Qemu__Executor:-jev}
export PlaceContext__Qemu__Agent__Provider=${PlaceContext__Qemu__Agent__Provider:-codex}
export PlaceContext__Qemu__Agent__Model=${PlaceContext__Qemu__Agent__Model:-gpt-6-astra}
# A hyphen in the profile name is valid in the process environment, but not in a shell variable.
exec env "PlaceContext__Qemu__Images__browser-linux=$image_path" \
  dotnet run --project "$repo_dir/src/PlaceContext.Host" --no-launch-profile -- \
  --environment Development --urls http://localhost:7700 "$@"
