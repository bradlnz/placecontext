while [ ! -f /out/.done ]; do sleep 1; done
curl -fsS -X PUT --upload-file /out/deps.tar.gz "$PCDEPS_PUT_URL"
