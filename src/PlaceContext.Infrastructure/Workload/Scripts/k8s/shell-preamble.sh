if {{GUARD}}; then
  mkdir -p {{DEPS_ROOT}}
  {{ENV}}
  if [ ! -f {{BAKED_MARKER}} ]; then
    {{INSTALL}} || exit $?
  fi
fi
