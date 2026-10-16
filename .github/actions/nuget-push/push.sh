#!/usr/bin/env bash
# Pushes every package in a folder to nuget.org with the key in NUGET_API_KEY. A tool package that points at
# per-platform packages goes after every other: pushed before them, it would install nothing until they arrive.

set -euo pipefail

folder="${1:?the folder holding the packages}"
source="https://api.nuget.org/v3/index.json"

shopt -s nullglob
packages=("$folder"/*.nupkg)

if [ "${#packages[@]}" -eq 0 ]; then
  echo "::error::no package in $folder: the release job uploads them as an artifact whose name starts with packages"
  exit 1
fi

first=()
last=()

for package in "${packages[@]}"; do
  settings="$(unzip -p "$package" 'tools/*/any/DotnetToolSettings.xml' 2>/dev/null || true)"

  if [[ "$settings" == *"<RuntimeIdentifierPackages>"* ]]; then
    last+=("$package")
  else
    first+=("$package")
  fi
done

for package in ${first[@]+"${first[@]}"} ${last[@]+"${last[@]}"}; do
  echo "::group::$(basename "$package")"
  dotnet nuget push "$package" --api-key "$NUGET_API_KEY" --source "$source" --skip-duplicate
  echo "::endgroup::"
done
