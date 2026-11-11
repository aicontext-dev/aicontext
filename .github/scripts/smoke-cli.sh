#!/usr/bin/env bash
# Smoke test of an aicontext executable. The same checks run for every distributed form: the standalone binaries and the
# tool from the NuGet package.
#
#   smoke-cli.sh <executable> [<expected-version>]
#
# It runs from the repository root: the last check validates this repository's own .aicontext/ strictly.
set -euo pipefail

bin="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
expected="${2:-}"
cd "$(dirname "$0")/../.."

"$bin" --help > /dev/null
version="$("$bin" --version)"
echo "$version"
reported="$(printf '%s\n' "$version" | head -n 1)"
if [ -n "$expected" ] && [ "$reported" != "aicontext $expected" ]; then
  echo "::error::$bin reports '$reported', expected 'aicontext $expected'."
  exit 1
fi

work="$(mktemp -d)"
ext="$(mktemp -d)"
bad="$(mktemp -d)"
trap 'rm -rf "$work" "$ext" "$bad"' EXIT

"$bin" init --path "$work" --name smoke-test
"$bin" validate --path "$work"
"$bin" export --path "$work" --target all
"$bin" export --path "$work" --target all --check
"$bin" validate --strict

# A fresh extended project is structurally complete, so plain validation passes, but strict
# validation reports its TODO placeholders until they are replaced.
"$bin" init --path "$ext" --name smoke-ext --profile extended
"$bin" validate --path "$ext"
if "$bin" validate --path "$ext" --strict; then
  echo "::error::Strict validation accepted the TODO placeholders of a fresh extended project."
  exit 1
fi

# Schema validation must still reject invalid input (in the trimmed binary too).
"$bin" init --path "$bad" --name smoke-bad
echo '{"specVersion":"0.1.0","project":{}}' > "$bad/.aicontext/context.json"
if "$bin" validate --path "$bad"; then
  echo "::error::validate accepted an invalid context.json."
  exit 1
fi

echo "Smoke test passed for $bin."
