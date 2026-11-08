# Changelog

All notable changes to the AIContext specification repository will be documented in this file.

Versions in this file are **tool/package versions** (`AIContext`, `AIContext.CLI`, standalone binaries). The specification has its own version (`0.1.0-draft`, `specVersion` `0.1.0`); format changes are marked as such below. See [SPEC.md section 9.1](./SPEC.md#91-specification-version-vs-tool-version).

## [Unreleased]

### Added

- **`aicontext status` shows overlap between exported files.** Some tools read several of the exported files and load them together (Copilot in VS Code combines `.github/copilot-instructions.md`, `AGENTS.md` and `CLAUDE.md`), so the per-target table understated the real always-on context. `status` now adds an "Overlap" section with the summed always-on size for such tools (Copilot, Cursor, Devin), built from a small table taken from the vendors' documentation and labelled as an estimate. It is informational only, not a `validate` warning. Library: `ToolOverlaps`, `ToolOverlap`, `StatusReport.Overlaps`.
- **`validate` reports obvious placeholder text** (`TODO: ...`, a value that is only `TODO` such as `**Tech stack:** TODO`, the starter summary line of `init`) in `README.md`, the section files and `rules/*.md` as the warning `AIC1107`. Code blocks and inline code are ignored, and a line can opt out with `aicontext:allow-placeholder`. Library: `PlaceholderScanner`, `DiagnosticCodes.PlaceholderContent`.
- `aicontext init` prints a hint to replace the `TODO` placeholders, and a successful `validate` states that it checks structure and obvious placeholders, not whether the content is useful or up to date.
- **Language-neutral conformance suite** in [conformance/](./conformance/README.md) for the draft format `0.1.0`: 20 `context.json` examples (4 valid, 16 invalid, including the rules JSON Schema cannot express) with the expected outcome and, for invalid ones, the JSON Pointer where an error is expected. Independent implementations can run it, and the reference implementation runs it in its tests (which also check that every fixture is listed in the manifest).
- **Help for projects that already have instruction files.** `aicontext init` lists the hand-written instruction files it finds (`AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, ...) and points to the migration recipe; a failed `export` because of such a file points to it as well. Nothing about overwriting changes. Library: `ProjectInitializer.FindExistingInstructionFiles`. The recipe in the user manual (chapter 11, B) is now a step-by-step procedure.
- **README and website.** The front page and the website now start with the problem, the result, who the project is for (and not for), how existing files are handled, which tools are supported and with what limits, a short real example and links to install and the guides.

### Changed

- **`validate --strict` now fails for a project that still contains starter placeholders.** A freshly initialized extended project used to pass `validate --strict` as the Extended profile although its files said `TODO`, and `export` sent those stubs to AI tools as real context. The profiles stay structural, a plain `validate` still passes (exit 0), and only `--strict` turns the warning into an error. The standalone-binary smoke test now expects exactly this.
- SPEC 7 and 8.2 state that placeholder detection is an optional heuristic and that the profiles describe structure only. No change to the format or to `specVersion`.
- GOVERNANCE 6.1 and 6.2 document the draft-period exception to the compatibility commitment (additive changes within `0.1.0`, as already described in SPEC 9.1) and what the maintainers plan to decide before the first stable release (immutable schema versions, how an implementation reacts to newer documents, the conformance suite). SPEC 14 links to the suite. No change to the format or to `specVersion`.

## [0.5.0] - 2026-11-08

This release hardens the CLI and the export. It fixes problems that could delete or write files outside the intended place, and problems that let a broken or mistyped invocation pass silently in CI. Some of these fixes change behavior that scripts may have relied on: see **Changed** first.

### Changed

- **The CLI rejects invalid arguments before running a command** (exit code 1, nothing is validated, exported or written). Previously unknown options were ignored, so `validate --strcit` passed without `--strict`, `export --chek` wrote files instead of checking them, and `init --path --force` (value missing) ran in the current directory and overwrote existing files. Now an unknown option, an option that belongs to another command, an option without its value, a repeated option and a non-boolean value after a flag (`--strict no`) are errors. Flags still accept an explicit `true`/`false`/`1`/`0`. Scripts that relied on the old lenient behavior must be corrected.
- The CLI logic moved from top-level statements to `AIContext.Cli.CliApp` (`CliApp.Run(args, output, error, workingDirectory)`) so it can be tested in-process. Output and exit codes of valid commands are unchanged; the overview from `--help` lists the commands in usage order.
- **`export` and `status` refuse what `validate` reports as an error.** Previously `export` only checked that the files exist and parse, so an unsupported `specVersion` (for example `999.0.0`), a wrong type, an unknown property or a `context.json` that was not an object still produced files while `validate` failed. Now `ContextLoader.Load` applies the same checks as `ValidateDirectory` and returns its errors; warnings (missing recommended files, possible secrets, ...) never block export or status. `export` prints every such error with the `ERROR:` prefix followed by `No files were written.`
- SPEC 6.3, 6.4 and 7 state the new requirements (no writes outside the project, separate projects, export applies validation errors). No change to the format or to `specVersion`.
- **Export and status are driven by the new `ExportService`; the CLI only prints.** Output of valid commands is unchanged. Errors that were printed bare now carry the `ERROR:` prefix and are followed by `No files were written.`: unsupported export target, missing target, unsupported `exports.defaultTarget`, unsupported layout (and `ERROR:` for the same problems in `status`). The extra line `Run 'aicontext validate' to see full details.` for an invalid `exports.targets` is gone (the schema check now catches those cases first).
- **Library result records changed shape.** `ContextValidationResult(Diagnostics, ExtendedProfile)`, `RuleLoadResult(Rules, Diagnostics)` and `ContextLoadResult(Loaded, Diagnostics)` carry a list of diagnostics; `Errors` and `Warnings` remain as read-only views with exactly the same messages as before. Code that only reads these properties is unaffected; code that constructs the records must be updated.

### Added

- `aicontext <command> --help` (also `-h`) prints the usage and options of a single command.
- CLI tests for argument validation, exit codes and the `init` / `validate` / `export --check` workflow. The test job in `pr-checks.yml` now runs on Ubuntu and Windows.
- `ExportFileStatus.Blocked` and `SyncState.Blocked` (with `ExportFilePlan.Reason`), and `ContextSchema.CanonicalUrl`, in the core library. `aicontext status` shows `blocked` for a target whose files would leave the project through a link.
- `SecretScanner.ScanDirectoryReport` (with `SecretScanReport` and `UnscannedFile`) returns the findings together with the files it could not scan; `ScanDirectory` still returns only the findings. `validate` warns `Secret scan incomplete for .aicontext/<file>: <reason>. Check it manually.` for such files, so `--strict` no longer passes a scan that was incomplete.
- **Stable diagnostic codes.** Every validation, rule, secret-scan and export problem is a `Diagnostic` (`Code`, `Severity`, `Message`, `Location`) with a code `AIC1001` ... `AIC4102` defined in `DiagnosticCodes` and listed in the user manual. Codes are never reused or renumbered; messages may change. `ContextValidationResult`, `RuleLoadResult` and `ContextLoadResult` expose `Diagnostics`.
- **`ExportService`** in the core library: `Plan(ExportRequest)` loads and validates the project, resolves target and layout, plans every file and reports overwrite conflicts and paths leaving the project as diagnostics; `Apply(ExportPlan)` writes a plan without errors and refuses one with errors; `Status(basePath, layout)` returns context size and sync state per target. Front ends no longer have to assemble the safe procedure themselves. See the library README.

### Fixed

- **`validate` no longer skips large files when scanning for secrets.** The scanner silently ignored every file above 512 KiB, so the same secret failed `validate --strict` in a small file and passed in a larger one (reproduced). Files are now read line by line without a size limit; a file that cannot be read, or a line that is too long or complex to check in time, is reported instead of being skipped (or crashing the scan). Files with other extensions than `.md`, `.mdc`, `.json`, `.yml`, `.yaml` and `.txt` are still not scanned, which SPEC 10, the README and the user manual now state together with the fact that the scan is a heuristic and no guarantee.
- **Exporting a project no longer deletes the generated `AGENTS.md` of an independent project in a subdirectory.** The cleanup of orphaned nested `AGENTS.md` files walked the whole tree, so a parent export removed a nested project's own file (even without `nestedAgents`) and `export --check` in that project then failed. A directory that has its own `.aicontext/` is now a project boundary: the cleanup skips it, and `nestedAgents` keeps a rule scoped to it (or below it) in the root file, with the reason shown by `status`. Orphan cleanup elsewhere is unchanged.

### Security

- **Export never writes or deletes outside the project through a symlink or junction.** The check that a nested directory lies inside the repository compared path text only, so a link inside the repository could send a generated `AGENTS.md` anywhere (reproduced with a Windows junction); the same applied to fixed output paths such as `.github/` and to removing orphaned rule files. Every path to be written or deleted is now resolved (links followed, also dangling file links) and must stay inside the real project root; links that stay inside are allowed, and a project reached through a link works. A violation is reported as `Blocked`; as with an overwrite conflict, nothing is written and the command fails (also with `--dry-run` and `--check`). Verified on Windows (junctions) and Linux (symlinks).

## [0.4.0] - 2026-11-04

### Changed

- Documented that specification and tool versions are independent (SPEC 9.1, README, this file), including which tool version introduced which format changes. No change to the format or to `specVersion`.

### Added

- `aicontext --version` (also `-v` and `version`) prints the tool version and the specification version.
- `aicontext export` now uses `exports.defaultTarget` when `--target` is not given (`--target` always takes precedence; `all` means the targets from `exports.targets`; without either, export still fails with a message naming both options). The meaning of `defaultTarget` is now documented in SPEC 6.1; the schema is unchanged.
- `aicontext init --profile <core|extended>` (default `core`). `extended` also creates starter `architecture.md`, `decisions.md` and `constraints.md`, so the result passes `validate --strict` as the Extended profile. Init logic moved from the CLI to `ProjectInitializer` in the core library. No change to the format.
- English [user manual](./docs/user-manual.md), published on the website together with a "Documentation" section on the landing page.
- Standalone CLI binaries (self-contained, single-file, no .NET required) are attached to each GitHub Release for Windows x64, Linux x64/arm64 and macOS x64/arm64, together with `SHA256SUMS.txt`. Built and smoke-tested by the new `release-binaries.yml` workflow. The `AIContext.CLI` NuGet tool remains available. The binaries use partial trimming (`TrimMode=partial`, about 11 MB instead of about 36 MB for Windows x64); the smoke test also covers `--version`, the extended profile and rejection of an invalid `context.json`.

## [0.3.0] - 2026-11-02

### Added

- `exports.nestedAgents` (split layout): rules scoped to an exact directory are exported to nested `<directory>/AGENTS.md` files so tools that only read `AGENTS.md` load them on demand. Only exact directory scopes of existing directories are mapped; everything else stays in the root file. `status` lists nested files and why a rule was kept in the root file. Orphaned generated nested files are removed (the tree walk skips hidden, `node_modules`, `bin`, `obj`, `vendor`, `dist`, `build`, `target`, `venv` directories). See SPEC 6.4.

- **Rules and split export layout.** Optional `.aicontext/rules/*.md` with `description` and `paths` frontmatter, and `exports.layout: "single" | "split"` (also `aicontext export --layout`). In the split layout `README.md` and `constraints.md` stay in a small always-on file, while scoped and on-demand content is exported as native rule files: Cursor `.mdc` (`globs`, `description`), Copilot `.github/instructions/*.instructions.md` (`applyTo`), Claude `.claude/rules/*.md` (`paths`), Windsurf/Devin rules (`trigger`, `globs`, `description`). Where a tool cannot scope content (AGENTS.md, Aider; on-demand content in Copilot and Claude) it stays in the main file. See SPEC 4.4 and 6.4.
- `aicontext status` command: context size per tool (always on vs. scoped/on-demand, estimated tokens), export sync state, and a hint when the `split` layout would reduce always-on context.
- `ContextLoader` and `ExportStatus` in the core library (shared by `export` and `status`).
- `devin` export target (`.devin/rules/`). It is opt-in: excluded from the default `all` set, and `validate` warns when `windsurf` and `devin` are both enabled (Devin also loads `.windsurf/rules`).
- Generated rule files use the `aicontext-` prefix; `export` removes generated rule files whose source no longer exists and never touches other files.
- `validate` checks rule files (name, frontmatter, paths) and scans them for secrets.
- This repository now uses the split layout and a `tests` rule as a working example.
- `aicontext export --dry-run` and `aicontext export --check` (exit code 1 when exports are missing or out of date).
- Unit tests for determinism, line endings, safe-overwrite behavior, rules, and split exports.

### Changed

- `aicontext validate` now validates `context.json` against the real JSON Schema (embedded in the library from [schema/v1/context.schema.json](./schema/v1/context.schema.json), evaluated with JsonSchema.Net). This also catches unknown properties, wrong `specVersion`, duplicate/empty values, and invalid dates, which were previously not detected. Error messages now have the form `context.json <json-pointer>: <message>`.
- `ContextValidator.ValidateDirectory` signature simplified to `(basePath, canonicalSchemaUrl)`; `ContextValidationResult` now exposes `ExtendedProfile` instead of `LocalSchemaPath`. The "local schema file not found" warning was removed.
- CI (`pr-checks.yml`, `validate-aicontext.yml`) now runs `aicontext validate --strict` and `aicontext export --target all --check` through the CLI. The PowerShell script `scripts/validate-aicontext.ps1` (a hand-written subset of the same checks) was removed.
- Repository `.aicontext/` now includes `architecture.md`, `decisions.md`, `constraints.md`, `patterns.md` and `glossary.md` (Extended profile), and the `aider` target, so `CONVENTIONS.md` is kept in sync.
- SPEC 7 and 10 clarified (empty recommended files, schema embedding, secret lint behavior).
- Exports are now deterministic: the generation timestamp was removed from the generated header and replaced by `Source-hash: sha256:<hex>`; output always uses LF line endings regardless of OS. Unchanged files are no longer rewritten. Existing generated files are refreshed once on the next `export`.
- `export` no longer overwrites existing files that were not generated by aicontext (no `Auto-generated by aicontext export` header). It reports a conflict, writes nothing, and exits with code 1 unless `--force` is given.
- SPEC 6.1/6.2 updated (timestamp SHOULD NOT be included, source hash recommended) and new section 6.3 "Safe overwrite" added.
- Export logic (target paths, planning and writing) moved from the CLI into the core library ([ExportTargets.cs](./src/AIContext/ExportTargets.cs), [ExportWriter.cs](./src/AIContext/ExportWriter.cs)); the per-target builders are unified and no longer take a timestamp.
- All projects now multi-target `net9.0;net10.0`; the `aicontext` .NET tool package ships both, so it installs on either runtime.
- Test project runs on both `net9.0` and `net10.0`.
- CI and publish workflows install .NET 9 and .NET 10 SDKs.
- Repository `.aicontext` stack updated to `.NET 10` and generated exports refreshed.

### Fixed

- Boolean CLI flags without a value (for example `init --force`) were treated as `false`.

## [0.2.0] - 2026-11-01

### Changed

- NuGet package versions bumped to `0.2.0` in [AIContext.csproj](./src/AIContext/AIContext.csproj), [AIContext.CLI.csproj](./src/AIContext.CLI/AIContext.CLI.csproj), and [AIContext.Spec.csproj](./src/AIContext.Spec/AIContext.Spec.csproj).
- `aicontext export --target all` now resolves target list from `exports.targets` in `.aicontext/context.json` and falls back to all supported targets when not configured.
- CLI package is configured as an installable .NET tool with command name `aicontext`.
- NuGet publish workflow switched to Trusted Publishing (OIDC via `NuGet/login@v1`) and no longer uses a long-lived `NUGET_API_KEY` secret.

### Added

- Export target resolution helper in [ExportTargetResolver.cs](./src/AIContext/ExportTargetResolver.cs).
- Unit tests for target resolution behavior in [ExportTargetResolverTests.cs](./tests/AIContext.Tests/ExportTargetResolverTests.cs).

## [0.1.0-draft] - 2026-11-01

### Added

- Initial public specification draft in [SPEC.md](./SPEC.md).
- Initial JSON Schema for `context.json` in [schema/v1/context.schema.json](./schema/v1/context.schema.json).
- Governance process draft in [GOVERNANCE.md](./GOVERNANCE.md).
- Initial repository `.aicontext` example files in [README.md](./.aicontext/README.md) and [context.json](./.aicontext/context.json).
- Validation script for local/CI checks in [validate-aicontext.ps1](./scripts/validate-aicontext.ps1).
- GitHub Actions validation workflow in [validate-aicontext.yml](./.github/workflows/validate-aicontext.yml).
- GitHub Pages deployment workflow in [pages.yml](./.github/workflows/pages.yml).
- Initial website pages in [docs/index.html](./docs/index.html) and [docs/spec/index.html](./docs/spec/index.html).
- Schema usage section in [README.md](./README.md) with canonical `$schema` URL example.
- Issue templates for bug reports, spec changes, and RFC proposals under [ISSUE_TEMPLATE](./.github/ISSUE_TEMPLATE/).
- Pull request CI workflow in [pr-checks.yml](./.github/workflows/pr-checks.yml) for context validation and .NET build checks.
- CLI MVP `init` and `validate` commands in [Program.cs](./src/AIContext.CLI/Program.cs).
- CLI project converted to executable app in [AIContext.CLI.csproj](./src/AIContext.CLI/AIContext.CLI.csproj).
- CLI `export --target agents` command to generate [AGENTS.md](./AGENTS.md) from `.aicontext` content.
- CLI `export --target all` now respects `exports.targets` in `.aicontext/context.json` (falls back to all supported targets when unset).
- First real additional export target: `copilot` output to [.github/copilot-instructions.md](./.github/copilot-instructions.md).
- Additional export target: `claude` output to [CLAUDE.md](./CLAUDE.md).
- Additional export target: `cursor` output to [.cursor/rules/project.mdc](./.cursor/rules/project.mdc).
- Additional export target: `windsurf` output to [.windsurf/rules/project.md](./.windsurf/rules/project.md).
- Additional export target: `aider` output to [CONVENTIONS.md](./CONVENTIONS.md).
- Export rendering logic extracted to core library in [ContextExporter.cs](./src/AIContext/ContextExporter.cs).
- Validation logic extracted to core library in [ContextValidator.cs](./src/AIContext/ContextValidator.cs).
- CLI package configured as installable .NET tool (`dotnet tool install --global AIContext.CLI`) in [AIContext.CLI.csproj](./src/AIContext.CLI/AIContext.CLI.csproj).
- New test project [AIContext.Tests](./tests/AIContext.Tests/AIContext.Tests.csproj) with exporter coverage.
- Added validation rule tests in [ContextValidatorTests.cs](./tests/AIContext.Tests/ContextValidatorTests.cs).
- PR workflow now runs tests in [pr-checks.yml](./.github/workflows/pr-checks.yml).
