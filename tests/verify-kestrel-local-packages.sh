#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-packages.XXXXXX")"
cli_home="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-cli-home.XXXXXX")"
tool_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-tool.XXXXXX")"
smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-templates.XXXXXX")"
config_file="$artifact_dir/nuget.config"

cleanup() {
  DOTNET_CLI_HOME="$cli_home" dotnet new uninstall Kestrel.Templates >/dev/null 2>&1 || true
  rm -rf "$artifact_dir" "$cli_home" "$tool_dir" "$smoke_root"
}
trap cleanup EXIT

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

public_projects=(
  "KSR.Core.csproj"
  "sdk/KSR.Build/KSR.Build.csproj"
  "sdk/KSR.Sdk/KSR.Sdk.csproj"
  "sdk/KSR.StdLib/KSR.StdLib.csproj"
  "sdk/KSR.Vision/KSR.Vision.csproj"
  "sdk/KSR.Creative/KSR.Creative.csproj"
  "sdk/KSR.Templates/KSR.Templates.csproj"
  "KSR.csproj"
)

for project in "${public_projects[@]}"; do
  dotnet pack "$repo_root/$project" --no-restore -c Release -o "$artifact_dir" -m:1 --nologo >/dev/null
done

expected_packages=(
  "Kestrel.0.1.0.nupkg"
  "Kestrel.Build.0.1.0.nupkg"
  "Kestrel.Core.0.1.0.nupkg"
  "Kestrel.Creative.0.1.0.nupkg"
  "Kestrel.Sdk.0.1.0.nupkg"
  "Kestrel.StdLib.0.1.0.nupkg"
  "Kestrel.Templates.0.1.0.nupkg"
  "Kestrel.Vision.0.1.0.nupkg"
)

for package in "${expected_packages[@]}"; do
  test -f "$artifact_dir/$package"
done

for package_path in "$artifact_dir"/*.nupkg; do
  package="$(basename "$package_path")"
  if [[ "$package" == KSR.* ]]; then
    echo "Unexpected legacy package produced: $package" >&2
    exit 1
  fi
done

printf '%s\n' \
  '<?xml version="1.0" encoding="utf-8"?>' \
  '<configuration>' \
  '  <packageSources>' \
  '    <clear />' \
  "    <add key=\"local-artifacts\" value=\"$artifact_dir\" />" \
  '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />' \
  '  </packageSources>' \
  '  <packageSourceMapping>' \
  '    <packageSource key="local-artifacts">' \
  '      <package pattern="Kestrel" />' \
  '      <package pattern="Kestrel.*" />' \
  '    </packageSource>' \
  '    <packageSource key="nuget.org">' \
  '      <package pattern="*" />' \
  '    </packageSource>' \
  '  </packageSourceMapping>' \
  '</configuration>' > "$config_file"

(cd "$artifact_dir" && DOTNET_CLI_HOME="$cli_home" dotnet tool install \
  --tool-path "$tool_dir" Kestrel --version 0.1.0 \
  --configfile "$config_file" --ignore-failed-sources --verbosity minimal >/dev/null)
test -x "$tool_dir/kestrel" || test -x "$tool_dir/kestrel.exe"

DOTNET_CLI_HOME="$cli_home" dotnet new install \
  "$artifact_dir/Kestrel.Templates.0.1.0.nupkg" --force >/dev/null

template_specs=(
  "kestrel-console|KestrelConsoleSmoke"
  "ksr-console|KsrConsoleSmoke"
  "kestrel-lib|KestrelLibrarySmoke"
  "ksr-lib|KsrLibrarySmoke"
  "kestrel-creative|KestrelCreativeSmoke"
  "ksr-creative|KsrCreativeSmoke"
  "kestrel-creative-camera|KestrelCameraSmoke"
  "ksr-creative-camera|KsrCameraSmoke"
)

for spec in "${template_specs[@]}"; do
  IFS='|' read -r template project_name <<< "$spec"
  project_dir="$smoke_root/$project_name"

  DOTNET_CLI_HOME="$cli_home" dotnet new "$template" -n "$project_name" -o "$project_dir" --force >/dev/null
  project_file="$(find "$project_dir" -maxdepth 1 -name '*.csproj' -print -quit)"
  test -n "$project_file"
  grep -Fq '<TargetFramework>net10.0</TargetFramework>' "$project_file"
  grep -Fq 'Kestrel.Sdk/0.1.0' "$project_file"
  if grep -Eq 'PackageReference Include="KSR\.' "$project_file"; then
    echo "Legacy KSR package reference found in $project_file" >&2
    exit 1
  fi

  DOTNET_CLI_HOME="$cli_home" dotnet restore "$project_file" \
    --configfile "$config_file" --ignore-failed-sources --nologo >/dev/null
  dotnet build "$project_file" --no-restore --nologo >/dev/null
done
