export HOME={deps}/home XDG_DATA_HOME={deps}/xdg NUGET_PACKAGES={deps}/nuget DOTNET_CLI_HOME={deps}/dotnet TMPDIR={deps}/tmp NUGET_FALLBACK_PACKAGES={deps}/nuget-seed
mkdir -p "$HOME" "$XDG_DATA_HOME" "$NUGET_PACKAGES" "$DOTNET_CLI_HOME" "$TMPDIR" {deps}/nuget-seed
