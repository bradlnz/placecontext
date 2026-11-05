while [ ! -f /out/.done ]; do
    if [ -f /out/.failed ]; then
        echo "Dependency preparation failed; skipping upload." >&2
        exit 1
    fi
    sleep 1
done
curl -fsS -X PUT --upload-file /out/deps.tar.gz "$PCDEPS_PUT_URL"
