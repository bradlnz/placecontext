set -e
trap 'status=$?; if [ "$status" -ne 0 ]; then touch /out/.failed; fi; exit "$status"' EXIT
mkdir -p /stage/.pcdeps /out
