#!/usr/bin/env bash
# Checks of the release input. publish.yml calls it, and every check can be run locally.
#
#   release-check.sh packages <packages-file>
#       Reads the version of each package from its project file and checks that AIContext and AIContext.CLI, which are
#       released together with the standalone binaries, have the same valid version. AIContext.Spec is versioned on its
#       own. Writes the list the other release scripts read, one "<id> <version> <policy>" line per package.
#   release-check.sh inputs <packages-file>
#       "packages", plus: a release is made from a tag named v<version>, and the tag points at RELEASE_SHA.
#   release-check.sh tag
#       The tag still points at RELEASE_SHA. A tag that moved since the run started would release other content.
#
# Environment:
#   RELEASE_MODE  publish or dry-run (default). In a dry run a problem that would stop a release is only a warning.
#   REF_TYPE, REF_NAME  github.ref_type and github.ref_name of the run.
#   RELEASE_SHA   github.sha, the commit that is released.
set -euo pipefail
cd "$(dirname "$0")/../.."

mode="${RELEASE_MODE:-dry-run}"
semver='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
problems=0
version=""
tag=""

# A problem in the repository itself, whatever the mode.
fail() {
  echo "::error::$*"
  exit 1
}

# A problem that stops a release. A dry run reports it and goes on.
problem() {
  if [ "$mode" = publish ]; then
    echo "::error::$*"
  else
    echo "::warning::Dry run. A release would stop here: $*"
  fi
  problems=$((problems + 1))
}

finish() {
  if [ "$mode" = publish ] && [ "$problems" -gt 0 ]; then
    exit 1
  fi
}

emit() {
  echo "$1=$2"
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    echo "$1=$2" >> "$GITHUB_OUTPUT"
  fi
}

project_version() {
  dotnet msbuild "$1" -getProperty:Version -nologo | tr -d '\r\n'
}

write_packages() {
  local file="$1" lib cli spec v
  lib="$(project_version src/AIContext/AIContext.csproj)"
  cli="$(project_version src/AIContext.CLI/AIContext.CLI.csproj)"
  spec="$(project_version src/AIContext.Spec/AIContext.Spec.csproj)"
  for v in "$lib" "$cli" "$spec"; do
    if [[ ! "$v" =~ $semver ]]; then
      fail "'$v' is not a valid package version (MAJOR.MINOR.PATCH with an optional -prerelease)."
    fi
  done
  if [ "$lib" != "$cli" ]; then
    fail "AIContext ($lib) and AIContext.CLI ($cli) are released together and must have the same version."
  fi
  printf '%s\n' "AIContext $lib release" "AIContext.CLI $cli release" "AIContext.Spec $spec independent" > "$file"
  version="$lib"
}

tag_unmoved() {
  local sha="${RELEASE_SHA:?RELEASE_SHA is required}" listing current
  if ! listing="$(git ls-remote origin "refs/tags/$REF_NAME" "refs/tags/$REF_NAME^{}")"; then
    problem "Could not read the tag '$REF_NAME' from the remote."
    return
  fi
  # An annotated tag is listed as the tag object and, peeled with ^{}, as its commit; a lightweight tag as the commit.
  current="$(printf '%s\n' "$listing" | awk -v r="refs/tags/$REF_NAME^{}" '$2 == r { print $1 }')"
  if [ -z "$current" ]; then
    current="$(printf '%s\n' "$listing" | awk -v r="refs/tags/$REF_NAME" '$2 == r { print $1 }')"
  fi
  if [ -z "$current" ]; then
    problem "The tag '$REF_NAME' does not exist on the remote."
  elif [ "$current" != "$sha" ]; then
    problem "The tag '$REF_NAME' points at $current, but this run started on $sha. The tag was moved, so nothing of this run may be released. Start again from the tag."
  fi
}

summary() {
  if [ -z "${GITHUB_STEP_SUMMARY:-}" ]; then
    return 0
  fi
  {
    echo "### Release input"
    echo
    echo "- Mode: $([ "$mode" = publish ] && echo 'publish' || echo 'dry run, nothing is published')"
    echo "- Commit: \`${RELEASE_SHA:-unknown}\`"
    echo "- Tag: \`${tag:-none (not started from a tag)}\`"
    while read -r id v policy; do
      echo "- $id $v ($policy)"
    done < "$1"
  } >> "$GITHUB_STEP_SUMMARY"
}

command="${1:-}"
case "$command" in
  packages)
    write_packages "${2:?usage: release-check.sh packages <packages-file>}"
    emit version "$version"
    ;;
  inputs)
    write_packages "${2:?usage: release-check.sh inputs <packages-file>}"
    if [ "${REF_TYPE:-}" = tag ]; then
      tag="${REF_NAME:?REF_NAME is required}"
      if [[ "$tag" != v* ]] || [[ ! "${tag#v}" =~ $semver ]]; then
        problem "The tag '$tag' is not a version tag (vMAJOR.MINOR.PATCH)."
      elif [ "$tag" != "v$version" ]; then
        problem "The tag is '$tag' but the projects are version $version. Tag the commit v$version, or fix the versions."
      fi
      tag_unmoved
    elif [ "$mode" = publish ]; then
      fail "A release is made from a tag. Start the workflow from the tag (Use workflow from: Tag), not from '${REF_NAME:-unknown}'."
    else
      echo "::notice::Dry run from '${REF_NAME:-unknown}', not from a tag: the tag checks are skipped."
    fi
    emit version "$version"
    emit tag "$tag"
    summary "$2"
    finish
    ;;
  tag)
    if [ "${REF_TYPE:-}" = tag ]; then
      REF_NAME="${REF_NAME:?REF_NAME is required}"
      tag_unmoved
      finish
    fi
    ;;
  *)
    echo "usage: release-check.sh packages|inputs <packages-file> | tag" >&2
    exit 2
    ;;
esac
