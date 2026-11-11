#!/usr/bin/env bash
# Pushes the packages of a plan exactly as they were packed and tested: no rebuild, and nothing is overwritten (there is
# no --skip-duplicate: a version that appeared in the meantime makes the push fail).
#
#   nuget-push.sh <nupkg-dir> <plan-file>
#
# <nupkg-dir> is the output of pack.sh. <plan-file> is the output of nuget-plan.sh.
# Environment: NUGET_API_KEY (required) and NUGET_SOURCE (default nuget.org; the tests push into a folder).
set -euo pipefail

dir="${1:?usage: nuget-push.sh <nupkg-dir> <plan-file>}"
plan="${2:?usage: nuget-push.sh <nupkg-dir> <plan-file>}"
source_url="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
: "${NUGET_API_KEY:?NUGET_API_KEY is required}"

# The files must be the ones pack.sh wrote and the smoke tests used.
(cd "$dir" && sha256sum --check --strict SHA256SUMS)

if grep -q '^blocked ' "$plan"; then
  echo "::error::The plan has blocked packages. Nothing is pushed."
  exit 1
fi

pushed=()
report() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    echo "::error::Stopped. Pushed before the failure: ${pushed[*]:-nothing}. Nothing is rolled back (see RELEASING.md)."
  fi
}
trap report EXIT

while read -r action id version; do
  case "$action" in
    push)
      dotnet nuget push "$dir/$id.$version.nupkg" --source "$source_url" --api-key "$NUGET_API_KEY"
      pushed+=("$id $version")
      ;;
    skip)
      echo "Skipped $id $version: it is already on nuget.org."
      ;;
  esac
done < "$plan"

echo "Pushed: ${pushed[*]:-nothing}"
