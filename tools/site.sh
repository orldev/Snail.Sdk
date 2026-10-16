#!/usr/bin/env bash
#
# The site of Snail.Sdk, built strictly the way docs.yml builds a library's: the API reference of the libraries written
# again from their assemblies, then MkDocs over docs/mkdocs.yml with the theme the SDK carries and the versions
# src/Docs/site/requirements.txt pins. site.yml runs it; so does anyone before a change to docs/ is committed.
#
#   tools/site.sh          builds the site into obj/site/site (SNAIL_SITE_FOLDER names another folder)
#   tools/site.sh serve    serves it on http://127.0.0.1:8000, built again on every change to docs/
#
# MkDocs runs from a virtual environment in obj/site/venv, made on the first run and again when requirements.txt
# changes; SNAIL_SITE_PYTHON names an interpreter that already has those packages instead. A broken link, a link to a
# missing heading or a page the navigation leaves out fails the build.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$root/obj/site"
requirements="$root/src/Docs/site/requirements.txt"

mkdir -p "$work"

# Every library with its public API written down gets its page in docs/reference (SnailSdkReference, Directory.Build.props).
for api in "$root"/src/*/PublicAPI.Shipped.txt; do
  library="$(dirname "$api")"
  dotnet build "$library/$(basename "$library").csproj" -c Release -nologo -v:q -p:SnailReference=true
done

# The configuration: docs/mkdocs.yml inherited, with the theme of src/Docs/site, written by the generator a library's
# SnailSite target runs.
docs="$(dotnet msbuild "$root/src/Docs/Docs.csproj" -nologo -p:Configuration=Release -getProperty:TargetPath)"
printf 'repository=%s\ntheme=%s\n' "$root" "$root/src/Docs/site/theme" > "$work/facts.txt"
dotnet "$docs" site "$work/facts.txt" "$work"

python="${SNAIL_SITE_PYTHON:-}"
if [ -z "$python" ]; then
  python="$work/venv/bin/python"
  if [ ! -x "$python" ] || ! cmp -s "$requirements" "$work/venv/requirements.txt"; then
    python3 -m venv --clear "$work/venv"
    "$python" -m pip install --quiet --disable-pip-version-check --requirement "$requirements"
    cp "$requirements" "$work/venv/requirements.txt"
  fi
fi

if [ "${1:-}" = serve ]; then
  exec "$python" -m mkdocs serve --strict --config-file "$work/mkdocs.yml"
fi

"$python" -m mkdocs build --strict --config-file "$work/mkdocs.yml" --site-dir "${SNAIL_SITE_FOLDER:-$work/site}"
