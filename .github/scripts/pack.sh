#!/usr/bin/env bash
# Packs AIContext, AIContext.CLI and AIContext.Spec once, and checks what was packed. These files are what the smoke
# tests use and what is pushed: nothing is packed again later.
#
#   pack.sh <output-dir>
#
# Writes into <output-dir>: the .nupkg files, release-packages.txt (see release-check.sh) and SHA256SUMS, which
# nuget-push.sh verifies right before it pushes. RELEASE_SHA (default: HEAD) is recorded in every package as the
# repository commit.
set -euo pipefail

out="$(mkdir -p "${1:?usage: pack.sh <output-dir>}" && cd "$1" && pwd)"
cd "$(dirname "$0")/../.."
sha="${RELEASE_SHA:-$(git rev-parse HEAD)}"

rm -f "$out"/*.nupkg "$out/release-packages.txt" "$out/SHA256SUMS"
bash .github/scripts/release-check.sh packages "$out/release-packages.txt"

for project in src/AIContext/AIContext.csproj src/AIContext.CLI/AIContext.CLI.csproj src/AIContext.Spec/AIContext.Spec.csproj; do
  dotnet pack "$project" -c Release -o "$out" --nologo -p:ContinuousIntegrationBuild=true -p:RepositoryCommit="$sha"
done

check_package() {
  local id="$1" version="$2" file entries nuspec entry
  file="$out/$id.$version.nupkg"
  if [ ! -f "$file" ]; then
    echo "::error::$file was not packed. The version in the project file and the one in the package differ."
    exit 1
  fi
  entries="$(unzip -Z1 "$file")"
  nuspec="$(unzip -p "$file" "$id.nuspec")"

  local expected
  for expected in "<id>$id</id>" "<version>$version</version>" "commit=\"$sha\"" '<license type="expression">MIT</license>' '<readme>README.md</readme>'; do
    if ! printf '%s\n' "$nuspec" | grep -Fq "$expected"; then
      echo "::error::$id $version: the package metadata lacks $expected."
      exit 1
    fi
  done

  local required=("README.md")
  if [ "$id" = AIContext.CLI ]; then
    required+=("tools/net9.0/any/DotnetToolSettings.xml" "tools/net10.0/any/DotnetToolSettings.xml" "tools/net9.0/any/AIContext.CLI.dll" "tools/net10.0/any/AIContext.CLI.dll")
  else
    required+=("lib/net9.0/$id.dll" "lib/net10.0/$id.dll")
  fi
  for entry in "${required[@]}"; do
    if ! printf '%s\n' "$entries" | grep -Fxq "$entry"; then
      echo "::error::$id $version: the package does not contain $entry."
      exit 1
    fi
  done
  echo "$id $version: packed and checked."
}

count=0
while read -r id version _policy; do
  check_package "$id" "$version"
  count=$((count + 1))
done < "$out/release-packages.txt"

stray="$(find "$out" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')"
if [ "$stray" -ne "$count" ]; then
  echo "::error::Expected $count packages in $out but found $stray."
  exit 1
fi

(cd "$out" && sha256sum ./*.nupkg | sed 's# \./# #' > SHA256SUMS)
cat "$out/SHA256SUMS"
