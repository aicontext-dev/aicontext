#!/usr/bin/env bash
# Decides, before anything is pushed, what happens to each package on nuget.org, and says so.
#
#   nuget-plan.sh <packages-file> <plan-file>
#
# <packages-file> has one "<id> <version> <policy>" line per package (release-check.sh writes it):
#   release      the version must not exist on nuget.org yet. A NuGet version cannot be replaced, so a release is never
#                pushed beside or over an existing one: the package is "blocked" and a real publish stops.
#   independent  pushed when the version is new. An existing version is skipped, and the skip is reported.
# <plan-file> gets one "<push|skip|blocked> <id> <version>" line per package; nuget-push.sh reads it.
#
# Environment: RELEASE_MODE (publish or dry-run; in a dry run a blocked package is only a warning) and
# NUGET_FLAT_CONTAINER (default https://api.nuget.org/v3-flatcontainer; the tests point it at a local server).
set -euo pipefail

packages="${1:?usage: nuget-plan.sh <packages-file> <plan-file>}"
plan="${2:?usage: nuget-plan.sh <packages-file> <plan-file>}"
mode="${RELEASE_MODE:-dry-run}"
base="${NUGET_FLAT_CONTAINER:-https://api.nuget.org/v3-flatcontainer}"
body="$(mktemp)"
trap 'rm -f "$body"' EXIT
problems=0
: > "$plan"

problem() {
  if [ "$mode" = publish ]; then
    echo "::error::$*"
  else
    echo "::warning::Dry run. A release would stop here: $*"
  fi
  problems=$((problems + 1))
}

lower() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

# Returns 0 when the version is on the feed, 1 when it is not, 2 when that cannot be told.
on_feed() {
  local id version code
  id="$(lower "$1")"
  version="$(lower "$2")"
  code="$(curl -sS -o "$body" -w '%{http_code}' --max-time 30 --retry 2 "$base/$id/index.json")" || return 2
  case "$code" in
    404) return 1 ;;
    200) grep -Fq "\"$version\"" "$body" ;;
    *) return 2 ;;
  esac
}

while read -r id version policy; do
  if [ -z "$id" ]; then
    continue
  fi
  rc=0
  on_feed "$id" "$version" || rc=$?
  case "$rc" in
    0)
      if [ "$policy" = independent ]; then
        echo "skip $id $version" >> "$plan"
        echo "::notice::$id $version is already on nuget.org. It is versioned on its own, so it is not pushed again."
      else
        echo "blocked $id $version" >> "$plan"
        problem "$id $version is already on nuget.org and a NuGet version cannot be replaced. Bump the version and tag again."
      fi
      ;;
    1)
      echo "push $id $version" >> "$plan"
      ;;
    *)
      echo "blocked $id $version" >> "$plan"
      problem "Could not find out whether $id $version is on nuget.org (the request to $base failed). Not guessing."
      ;;
  esac
done < "$packages"

cat "$plan"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### NuGet plan"
    echo
    sed 's/^/- /' "$plan"
  } >> "$GITHUB_STEP_SUMMARY"
fi

if [ "$mode" = publish ] && [ "$problems" -gt 0 ]; then
  exit 1
fi
