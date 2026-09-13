#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task7-packages.XXXXXX")"
cli_home="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task7-cli-home.XXXXXX")"
tool_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task7-tool.XXXXXX")"
project_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task7-project.XXXXXX")"
config_file="$artifact_dir/nuget.config"

cleanup() {
  dotnet new uninstall Kestrel.Templates >/dev/null 2>&1 || true
  rm -rf "$artifact_dir" "$cli_home" "$tool_dir" "$project_dir"
}
trap cleanup EXIT

dotnet pack "$repo_root/KSR.sln" --no-restore -c Release -o "$artifact_dir" -m:1 >/dev/null

test -f "$artifact_dir/Kestrel.0.1.0.nupkg"
test -f "$artifact_dir/Kestrel.Core.0.1.0.nupkg"
test -f "$artifact_dir/Kestrel.Build.0.1.0.nupkg"
test -f "$artifact_dir/Kestrel.Sdk.0.1.0.nupkg"
test -f "$artifact_dir/Kestrel.StdLib.0.1.0.nupkg"
test -f "$artifact_dir/Kestrel.Templates.0.1.0.nupkg"

cat > "$config_file" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-artifacts" value="$artifact_dir" />
  </packageSources>
</configuration>
EOF

(cd "$artifact_dir" && DOTNET_CLI_HOME="$cli_home" dotnet tool install --tool-path "$tool_dir" Kestrel \
  --version 0.1.0 --configfile "$config_file" --ignore-failed-sources >/dev/null)
test -x "$tool_dir/kestrel" || test -x "$tool_dir/kestrel.exe"

DOTNET_CLI_HOME="$cli_home" dotnet new install "$artifact_dir/Kestrel.Templates.0.1.0.nupkg" --force >/dev/null
DOTNET_CLI_HOME="$cli_home" dotnet new kestrel-console -o "$project_dir" --force >/dev/null
dotnet restore "$project_dir/MyApp.csproj" --configfile "$config_file" --ignore-failed-sources >/dev/null
dotnet build "$project_dir/MyApp.csproj" --no-restore >/dev/null
