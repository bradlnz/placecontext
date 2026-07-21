mkdir -p /out
{{DEPS_PREAMBLE}}cat /work/input.json | {{INVOKE}}
rc=$?
echo
echo {{ARTIFACTS_MARKER}}
find /out -type f 2>/dev/null | while read -r f; do
  printf '==PC-FILE== %s %s\n' "${f#/out/}" "$(wc -c < "$f" | tr -d ' \t')"
  base64 < "$f"
  echo {{FILE_END_MARKER}}
done
exit $rc