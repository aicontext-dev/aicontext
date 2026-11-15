# Decisions

## D1. One export target

Use only `agents` so the exercise focuses on source validation and stale output,
not differences between tool formats.

## D2. Verify rather than repair in CI

Generate output locally and review it with the source change.
CI checks the submitted files without regenerating them first.
