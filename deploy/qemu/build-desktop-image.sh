#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 2 ]]; then
  echo "Usage: $0 VERIFIED_DEBIAN_13_AMD64_CLOUD_IMAGE OUTPUT.qcow2" >&2
  exit 2
fi
for tool in qemu-img qemu-system-x86_64 cloud-localds timeout; do
  command -v "$tool" >/dev/null || { echo "Install $tool before building the desktop image." >&2; exit 1; }
done
base=$(realpath "$1")
output=$(realpath -m "$2")
[[ -f "$base" && ! -e "$output" ]] || { echo 'Input must exist and output must not already exist.' >&2; exit 1; }
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
mkdir -p -- "$(dirname -- "$output")"
work=$(mktemp -d "$(dirname -- "$output")/.desktop-build.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT
qemu-img create -f qcow2 -F qcow2 -b "$base" "$work/desktop.qcow2" 12G
printf 'instance-id: placecontext-desktop\nlocal-hostname: workflow-desktop\n' > "$work/meta-data"
cloud-localds "$work/seed.iso" "$script_dir/desktop-cloud-init.yaml" "$work/meta-data"
accel=tcg
cpu=max
if [[ -r /dev/kvm && -w /dev/kvm ]]; then accel=kvm; cpu=host; fi
echo "Preparing the non-root desktop with $accel; package installation may take up to 30 minutes."
# cloud-init powers off only after its provisioning checks succeed.
timeout --foreground 1800 qemu-system-x86_64 -no-user-config -nodefaults \
  -machine q35 -accel "$accel" -cpu "$cpu" -m 2048 -smp 2 \
  -drive "file=$work/desktop.qcow2,format=qcow2,if=virtio" \
  -drive "file=$work/seed.iso,format=raw,if=virtio,readonly=on" \
  -device virtio-vga -device virtio-rng-pci \
  -device virtio-serial -chardev "socket,path=$work/agent.sock,server=on,wait=off,id=agent" \
  -device virtserialport,chardev=agent,name=org.qemu.guest_agent.0 \
  -nic user,model=virtio-net-pci -display none -monitor none -serial stdio \
  > "$output.build.log" 2>&1
grep -Eq '(^|: )PC_DESKTOP_READY[[:space:]]*$' "$output.build.log" || { echo "Desktop setup did not finish; see $output.build.log" >&2; exit 1; }
qemu-img check "$work/desktop.qcow2"
qemu-img convert -O qcow2 "$work/desktop.qcow2" "$work/complete.qcow2"
# A hard link refuses to overwrite an image created by another builder.
ln -- "$work/complete.qcow2" "$output"
echo "Created $output. Configure it as PlaceContext:Qemu:Images:browser-linux."
