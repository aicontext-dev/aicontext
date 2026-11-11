#!/usr/bin/env bash
# The GitHub release side of a release: where the standalone binaries go. Nothing is ever replaced: an existing asset of
# these names stops the run, and "gh release upload" is called without --clobber.
#
#   release-assets.sh check <tag>
#       The release of the tag exists and has no asset of the names this workflow uploads
#       (aicontext-*, SHA256SUMS.txt, SOURCE.txt).
#   release-assets.sh upload <tag> <dir>
#       Writes SHA256SUMS.txt and SOURCE.txt next to the archives in <dir>, checks the release again and uploads
#       everything in <dir>.
#
# Environment: GH_TOKEN, GITHUB_REPOSITORY. RELEASE_MODE (publish or dry-run) for "check": in a dry run a problem is
# only a warning. "upload" is always a real publish and also needs RELEASE_SHA, RELEASE_VERSION, GITHUB_SERVER_URL and
# GITHUB_RUN_ID.
set -euo pipefail

mode="${RELEASE_MODE:-dry-run}"
problems=0

problem() {
  if [ "$mode" = publish ]; then
    echo "::error::$*"
  else
    echo "::warning::Dry run. A release would stop here: $*"
  fi
  problems=$((problems + 1))
}

check_release() {
  local tag="$1" names clash
  if ! names="$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json assets --jq '.assets[].name')"; then
    problem "There is no published GitHub release for the tag '$tag'. Create the release first: the workflow attaches the binaries to it."
    return
  fi
  clash="$(printf '%s\n' "$names" | grep -E '^(aicontext-.*|SHA256SUMS\.txt|SOURCE\.txt)$' | tr '\n' ' ' || true)"
  if [ -n "$clash" ]; then
    problem "The release '$tag' already has assets this workflow uploads: $clash. Assets are never overwritten. If they are really wrong, delete them on the release page and run the workflow again."
  fi
}

upload() {
  local tag="$1" dir="$2"
  : "${RELEASE_SHA:?RELEASE_SHA is required}" "${RELEASE_VERSION:?RELEASE_VERSION is required}"
  mode=publish

  (cd "$dir" && LC_ALL=C sha256sum aicontext-* > SHA256SUMS.txt)
  {
    echo "AIContext $RELEASE_VERSION"
    echo "Tag: $tag"
    echo "Commit: $RELEASE_SHA"
    echo "Built by: ${GITHUB_SERVER_URL:-https://github.com}/$GITHUB_REPOSITORY/actions/runs/${GITHUB_RUN_ID:-unknown}"
    echo
    echo "SHA256SUMS.txt lists the archives of this release. The NuGet packages of the same run carry the commit in"
    echo "their repository metadata. This file documents where the binaries come from; it is not a signature."
  } > "$dir/SOURCE.txt"

  # The check is repeated, because the release may have changed since the start of the run, and nothing is uploaded
  # if any asset of these names exists.
  check_release "$tag"
  if [ "$problems" -gt 0 ]; then
    exit 1
  fi
  gh release upload "$tag" "$dir"/* --repo "$GITHUB_REPOSITORY"
  echo "Uploaded to the release '$tag':"
  ls -1 "$dir"
}

case "${1:-}" in
  check)
    check_release "${2:?usage: release-assets.sh check <tag>}"
    if [ "$mode" = publish ] && [ "$problems" -gt 0 ]; then
      exit 1
    fi
    ;;
  upload)
    upload "${2:?usage: release-assets.sh upload <tag> <dir>}" "${3:?usage: release-assets.sh upload <tag> <dir>}"
    ;;
  *)
    echo "usage: release-assets.sh check <tag> | upload <tag> <dir>" >&2
    exit 2
    ;;
esac
