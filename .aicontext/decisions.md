Active decisions, not a change log. Keep these summaries brief; detailed rationale and history belong in `docs/decisions.md` (repository-root path). Before changing behavior covered by a decision, read its full D-number entry there.

### D1. Complement AGENTS.md instead of competing with it
Export into existing tool formats; `.aicontext/` is the canonical source, so adoption does not require vendor support.

### D2. Spec-first with a published JSON Schema
SPEC and schema define the contract, not the CLI. Keep changes and fixtures aligned; follow `GOVERNANCE.md` for draft decisions.

### D3. Deterministic exports
Use `LineEndings.Normalize` (CRLF and CR to LF), a source hash and no timestamps. Unchanged output is not rewritten, so CI can check drift.

### D4. Never overwrite files the tool did not generate
Require the generated header or explicit `--force` to protect hand-written files. Force does not bypass path safety.

### D5. Validation is warn-by-default, strict on demand
Errors block validation; warnings fail only with `validate --strict`. Incomplete context need not block adoption.

### D6. Multi-target .NET 9 and .NET 10
Libraries, CLI and tests target both runtimes, so either can run the tool.

### D7. Trusted Publishing for NuGet
One verified release commit produces the exact artifacts published by `publish.yml`. Use OIDC, never replace published content; procedure: `RELEASING.md`.

### D8. Scoped rules and a split export layout
Split uses native scopes/on-demand loading where supported. Copilot/Claude keep on-demand content inline; Agents/Aider keep all content. Split alone does not bound context size.

### D9. Devin is a separate, opt-in target
Exclude Devin from default `all`; it also reads Windsurf rules, so enabling both can duplicate context.

### D10. Nested AGENTS.md is opt-in and exact-directory only
Map only exact existing directory scopes in split layout. Never create directories or broaden a rule; nested output may duplicate native rules.

### D11. A directory with its own `.aicontext/` is a separate project
Export respects nested project boundaries, including through links, so projects can export independently.

### D12. Export never writes or deletes through a link that leaves the project
Init/export share the component-wise path guard: both readings of `..` must be safe, and case equality comes from directory entries, not OS names. Reverify stale plans before writing. Hard links and check/write races remain limits.

### D13. Export and status refuse what validate reports as an error
Use the same input error checks; warnings never block export/status. Strict mode belongs to validation only.

### D14. Diagnostics have stable codes and the library has one entry point for exporting
Keep diagnostic codes stable. Integrations use `ExportService.Plan/Apply/Status` rather than rebuilding the safe procedure.

### D15. Profiles are structural; validation reports obvious placeholders as warnings
Core/Extended describe structure, not usefulness. Placeholder lint (`AIC1107`) warns; strict validation fails.

### D16. `status` reports overlap between exported files; `validate` does not
Overlap is a vendor-documented estimate, not observed loading or a validation error. Recheck evidence when tool behavior changes.

### D17. Generated files of a dropped target are reported, never deleted
Status and full-configuration exports warn (`AIC4201`); explicit single-target export does not. No automatic deletion or check-exit change.

### D18. A single file is written safely; an export is not a transaction
Preflight known failures; create exclusively and replace regular files via a temporary file. File symlinks are written in place and can be damaged by interruption. Stop on failure, report completed writes; no rollback.

### D19. Sources must be inside the project; text taken from a file is not repeated when it looks like a secret
Read through `ContextFileReader`; refuse external sources (`AIC1008`). Redact quoted file text with `SecretScanner.Redact`. Scanning is heuristic; hard-linked sources remain readable.

### D20. Rule frontmatter is a narrow, exactly specified subset, and a header that is unclear never widens a rule
Follow SPEC 4.4.1 and conformance fixtures, not a YAML parser. Invalid headers error. Unknown keys warn and are ignored: `Paths` is not `paths` and can leave a rule always on.

### D21. A path pattern has one meaning for every tool, and a scope a tool cannot be told is refused, never widened
SPEC 4.4.2 defines root-anchored globs. Use documented translations or refuse (`AIC4107`); vendor dot-name/case behavior remains unverified.

### D22. The description of a scoped rule is never written as a field a tool documents as a second trigger
Copilot scopes use `applyTo` only; keep description in the body. Apply this to other tools only with evidence of an independent trigger.
