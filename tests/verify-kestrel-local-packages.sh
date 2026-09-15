#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifact_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-packages.XXXXXX")"
cli_home="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-cli-home.XXXXXX")"
tool_dir="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-tool.XXXXXX")"
smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-templates.XXXXXX")"
nuget_packages="$(mktemp -d "${TMPDIR:-/tmp}/kestrel-task5-nuget-packages.XXXXXX")"
config_file="$artifact_dir/nuget.config"

cleanup() {
  DOTNET_CLI_HOME="$cli_home" dotnet new uninstall Kestrel.Templates >/dev/null 2>&1 || true
  rm -rf "$artifact_dir" "$cli_home" "$tool_dir" "$smoke_root" "$nuget_packages"
}
trap cleanup EXIT

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$nuget_packages"

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
  dotnet pack "$repo_root/$project" --no-restore -c Release -o "$artifact_dir" -m:1 --nologo
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

# Creative and camera packages intentionally keep their upstream runtime
# dependencies. Copy the exact already-restored nupkgs into this temporary
# feed so the matrix remains completely offline and reproducible on the host.
nuget_cache="${NUGET_PACKAGES_SOURCE:-$HOME/.nuget/packages}"
third_party_packages=(
  "raylib-cs/7.0.2/raylib-cs.7.0.2.nupkg"
  "opencvsharp4/4.13.0.20260302/opencvsharp4.4.13.0.20260302.nupkg"
  "opencvsharp4.runtime.win/4.13.0.20260302/opencvsharp4.runtime.win.4.13.0.20260302.nupkg"
  "system.memory/4.6.3/system.memory.4.6.3.nupkg"
)

for package in "${third_party_packages[@]}"; do
  package_path="$nuget_cache/$package"
  if [[ ! -f "$package_path" ]]; then
    echo "Required offline dependency is not present in the host NuGet cache: $package_path" >&2
    exit 1
  fi
  cp "$package_path" "$artifact_dir/"
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
  '  </packageSources>' \
  '</configuration>' > "$config_file"

(cd "$artifact_dir" && DOTNET_CLI_HOME="$cli_home" dotnet tool install \
  --tool-path "$tool_dir" Kestrel --version 0.1.0 \
  --configfile "$config_file" --ignore-failed-sources --verbosity minimal)
test -x "$tool_dir/kestrel" || test -x "$tool_dir/kestrel.exe"

DOTNET_CLI_HOME="$cli_home" dotnet new install \
  "$artifact_dir/Kestrel.Templates.0.1.0.nupkg" --force

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

  DOTNET_CLI_HOME="$cli_home" dotnet new "$template" \
    -n "$project_name" -o "$project_dir" --force --no-update-check
  project_file="$(find "$project_dir" -maxdepth 1 -name '*.csproj' -print -quit)"
  test -n "$project_file"
  # The template carries a developer-facing nuget.config for normal projects.
  # Replace it in this isolated smoke project so the MSBuild SDK resolver and
  # package restore use exactly the temporary local feed below.
  cp "$config_file" "$project_dir/nuget.config"
  grep -Fq '<TargetFramework>net10.0</TargetFramework>' "$project_file"
  grep -Fq 'Kestrel.Sdk/0.1.0' "$project_file"
  if grep -Eq 'PackageReference Include="KSR\.' "$project_file"; then
    echo "Legacy KSR package reference found in $project_file" >&2
    exit 1
  fi

  echo "Restoring $template ($project_name) from isolated local feed"
  DOTNET_CLI_HOME="$cli_home" dotnet restore "$project_file" \
    --configfile "$config_file" --ignore-failed-sources --nologo
  echo "Building $template ($project_name) on net10.0"
  dotnet build "$project_file" --no-restore --nologo
done
