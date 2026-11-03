# AIContext Specification

Status: DRAFT  
Version: 0.1.0-draft  
Last updated: 2026-11-03

---

## 1. Purpose

AIContext defines an open, tool-agnostic project context format for AI coding assistants.

It provides:

- one canonical source directory (`.aicontext/`)
- human-readable project knowledge
- deterministic export into tool-specific instruction formats

AIContext complements existing formats (such as `AGENTS.md`, `CLAUDE.md`, and Copilot instructions). It does not replace source code, ADR repositories, or internal architecture docs.

## 2. Design goals

1. **Single source of truth**: context is maintained in one structured location.
2. **Tool interoperability**: context can be exported to multiple AI tools.
3. **Versionability**: context changes are tracked in git like code changes.
4. **Human-first readability**: markdown content is easy to review and edit.
5. **Progressive adoption**: minimal required files, optional deeper detail.

## 3. Repository layout

AIContext lives in a project-local directory:

```text
<repo-root>/
  .aicontext/
    README.md
    context.json
    architecture.md
    decisions.md
    patterns.md
    constraints.md
    glossary.md
    rules/
      ui.md
      connectors.md
```

`rules/` is optional and holds scoped rules (see 4.4).
## 4. File roles and requirement levels

### 4.1 Required files

#### `README.md` (required)

High-level project summary for AI assistants:

- project purpose
- primary stack
- architecture style
- testing approach

#### `context.json` (required)

Machine-readable metadata:

- schema and spec version
- project identity
- optional export configuration

### 4.2 Recommended files

#### `architecture.md` (recommended)

Architecture rationale ("why"), not only topology ("what").

#### `decisions.md` (recommended)

Key architecture/product decisions in ADR-like format, including trade-offs.

#### `constraints.md` (recommended)

Hard rules and "must not" constraints that prevent recurring AI mistakes.

### 4.3 Optional files

#### `patterns.md` (optional)

Preferred coding patterns to reuse.

#### `glossary.md` (optional)

Domain terminology that AI models commonly misunderstand.

### 4.4 Rules (optional)

`rules/*.md` hold guidance that only matters for part of the codebase (for example UI code or integration connectors). Rules are tool-agnostic: the author declares *where* a rule applies and exporters translate that to each tool's native mechanism.

Each rule is a markdown file directly inside `.aicontext/rules/`. The file name (without `.md`) is the rule name; it MUST consist of lowercase letters, digits, `-` or `_`, and MUST NOT be one of the reserved names `architecture`, `decisions`, `patterns`, `constraints`, `glossary`, `project`.

A rule MAY start with a frontmatter block:

```markdown
---
description: UI conventions for React components
paths: ["src/ui/**", "**/*.tsx"]
---

Use the shared design-system components.
```

| Key | Meaning |
| --- | --- |
| `paths` | Glob patterns (`/` separators). The rule applies when working with matching files. Accepts a list or a comma-separated string. |
| `description` | One line explaining when the rule is relevant. |

The rule kind follows from the frontmatter:

- `paths` present: **scoped**, applies only to matching files.
- only `description` present: **on-demand**, loaded when the agent decides it is relevant.
- neither: **always on**.

Implementations MUST ignore unknown frontmatter keys (and SHOULD warn about them). Implementations SHOULD treat `architecture.md`, `decisions.md`, `patterns.md` and `glossary.md` as on-demand content and `README.md` and `constraints.md` as always-on content when they apply a split layout (see 6.4).
## 5. `context.json` format (v0.1 draft)

Minimum valid shape:

```json
{
  "$schema": "https://aicontext.dev/schema/v1/context.schema.json",
  "specVersion": "0.1.0",
  "project": {
    "name": "MyApp"
  }
}
```

Extended example:

```json
{
  "$schema": "https://aicontext.dev/schema/v1/context.schema.json",
  "specVersion": "0.1.0",
  "project": {
    "name": "MyApp",
    "description": "B2B e-commerce platform",
    "primaryLanguage": "C#",
    "stack": [".NET 9", "EF Core", "React", "PostgreSQL"]
  },
  "exports": {
    "targets": ["agents", "copilot", "claude", "cursor", "windsurf", "aider"],
    "defaultTarget": "all",
    "layout": "split"
  },
  "metadata": {
    "lastReviewed": "2026-11-01"
  }
}
```

## 6. Export model

`.aicontext/` is canonical. Generated tool files are derived artifacts.

Implementations MUST treat exports as generated output and SHOULD include a generated-file header with:

- source path (`.aicontext/`)
- a hash of the source content (RECOMMENDED, e.g. `Source-hash: sha256:<hex>`), which identifies the input without breaking determinism
- tool target

Implementations SHOULD NOT include a generation timestamp, because it makes every run produce a different file (see 6.2). A timestamp MAY be included only when the implementation offers it as an explicit opt-in.

### 6.1 Export targets

- `agents` -> `AGENTS.md`
- `copilot` -> `.github/copilot-instructions.md` (and `.github/instructions/*.instructions.md` in the split layout)
- `claude` -> `CLAUDE.md` (and `.claude/rules/*.md` in the split layout)
- `cursor` -> `.cursor/rules/*.mdc`
- `windsurf` -> `.windsurf/rules/*.md`
- `devin` -> `.devin/rules/*.md`. Devin also loads `.windsurf/rules`, so enabling both `windsurf` and `devin` loads the same rules twice; implementations SHOULD warn about this and SHOULD NOT include `devin` in a default "all targets" set.
- `aider` -> `CONVENTIONS.md` (optional)

### 6.2 Deterministic output

For identical input and configuration, export output SHOULD be deterministic to minimize noisy diffs. In particular, output SHOULD NOT depend on the current time, the host operating system, or line-ending settings (LF is RECOMMENDED), and implementations SHOULD NOT rewrite files whose content would not change.

### 6.3 Safe overwrite

Implementations MUST NOT silently overwrite an existing file that was not generated by the implementation (a file without the generated-file header). They SHOULD refuse and require an explicit user override. Implementations SHOULD offer a dry-run mode and a check mode that reports out-of-date exports (for CI).

### 6.4 Layouts

`exports.layout` selects how content is distributed over files:

- `single` (default): one file per tool containing all content, including rules.
- `split`: a small always-on main file plus separate files for scoped and on-demand content, using each tool's native rule mechanism.

In the split layout, always-on content (`README.md`, `constraints.md`, always-on rules) stays in the main file. Scoped rules and on-demand content are written as separate files where the tool supports it:

| Tool | Scoped rules | On-demand content |
| --- | --- | --- |
| Cursor | `.cursor/rules/aicontext-<name>.mdc` with `globs` | separate `.mdc` with `description`, `alwaysApply: false` |
| Copilot | `.github/instructions/aicontext-<name>.instructions.md` with `applyTo` | kept in the main file |
| Claude | `.claude/rules/aicontext-<name>.md` with `paths` | kept in the main file |
| Windsurf, Devin | `aicontext-<name>.md` with `trigger: glob` and `globs` | `trigger: model_decision` with `description` |
| Agents, Aider | no scoping; rules are rendered as sections in the single file | kept in the single file |

Content is never dropped: where a tool cannot express a scope, the content is kept in the main file, with the scope stated in text (for example `Applies to: src/ui/**`).

#### Nested `AGENTS.md` (optional)

`AGENTS.md` has no scoping of its own, but tools that read it also read an `AGENTS.md` located in a subdirectory when working there. With `exports.nestedAgents: true` and the split layout, an implementation MAY write directory-scoped rules to `<directory>/AGENTS.md` instead of the root file. This applies only to rules whose every pattern is an exact directory scope (`<dir>/**`, `<dir>/**/*`, `<dir>/` or `<dir>`) and whose directories exist; all other rules stay in the root file. Implementations MUST NOT create directories for this purpose and SHOULD report which rules were kept in the root file and why. Orphaned generated nested files MAY be removed using the same header rule as other generated files.

Because several tools read both `AGENTS.md` and their own rule files, enabling `nestedAgents` together with other scoping targets (for example `cursor` or `copilot`) can make a tool see the same rule twice.

Generated rule files MUST be named with the `aicontext-` prefix, so they never collide with hand-written rule files, and MUST carry the generated-file header. When a generated rule file no longer corresponds to a source, implementations SHOULD remove it, and MUST NOT remove files that lack the prefix or the header.

## 7. Validation levels

Validation is divided into three levels:

1. **Structural** (MUST): required files exist and are readable.
2. **Schema** (MUST): `context.json` is valid against declared schema.
3. **Quality** (SHOULD): recommended files are non-empty and materially useful.

Recommended-file absence is not a hard spec error in v0.1, but MUST be surfaced as warnings. Implementations SHOULD also warn about recommended files that exist but are empty.

Schema validation MUST use the published schema for the declared `specVersion`. Implementations MAY embed a copy of the schema instead of fetching it.

## 8. Compliance profiles

### 8.1 Core profile (minimum compliant)

A repository is Core-compliant if:

- `.aicontext/README.md` exists and is non-empty
- `.aicontext/context.json` exists and passes schema validation

### 8.2 Extended profile

A repository is Extended-compliant if Core profile is met and all recommended files exist and are non-empty:

- `architecture.md`
- `decisions.md`
- `constraints.md`

## 9. Compatibility and versioning

AIContext specification versioning follows Semantic Versioning:

- **PATCH**: editorial clarifications; no format behavior changes
- **MINOR**: backward-compatible additions
- **MAJOR**: breaking changes

Tooling SHOULD reject unknown MAJOR versions by default and MAY offer explicit override.

### 9.1 Specification version vs. tool version

The specification and the reference tooling are versioned independently:

- **Specification version** (`Version` in this file, `specVersion` in `context.json`, the schema under `schema/v1/`) identifies the format. It changes only when the format changes.
- **Tool version** (the `AIContext` and `AIContext.CLI` packages and the standalone binaries) identifies a software release. It also changes for bug fixes and features that do not alter the format.

While the specification is a draft (`0.x`), additions may be made within the current version and `specVersion` stays `0.1.0`. The first change that makes existing files invalid or changes their meaning bumps the specification version, and the schema is widened so that files declaring older supported versions remain valid.

| Tool version | Specification version | Format changes introduced |
|--------------|-----------------------|---------------------------|
| 0.1.0 – 0.2.0 | 0.1.0-draft | Initial format |
| 0.3.0 | 0.1.0-draft | Rules (4.4), safe overwrite (6.3), layouts and nested `AGENTS.md` (6.4), `devin` target |

## 10. Security and privacy guidance

`.aicontext/` SHOULD NOT include:

- secrets (tokens, passwords, private keys)
- customer personal data
- confidential internal data not intended for repository readers

Implementations SHOULD provide lint checks for common secret patterns in context files. Such findings SHOULD be reported as warnings (and MAY be promoted to errors by an explicit strict mode), SHOULD NOT print the matched secret value, and SHOULD offer a way to mark intentional examples.

## 11. Governance (project-level)

Specification changes SHOULD use an RFC-like process:

1. Proposal issue (`RFC:` prefix)
2. Public discussion period
3. Pull request updating `SPEC.md` and related schema
4. Version bump and changelog update

Repository governance details are documented in `GOVERNANCE.md`.

## 12. Non-goals (v0.1)

v0.1 does not define:

- domain-specific ontology formats
- automatic context extraction quality guarantees
- LLM runtime APIs/protocols
- policy enforcement in target AI tools

## 13. Example `.aicontext/README.md`

```markdown
# Project: MyApp

B2B e-commerce platform built on .NET and React.

**Tech stack:** .NET 9, EF Core, React 18, PostgreSQL
**Architecture:** Vertical Slice Architecture
**Testing:** xUnit + Playwright
```

## 14. Implementation notes for CLI/tooling

Planned command surface:

- `aicontext init`
- `aicontext validate`
- `aicontext export --target <target|all>`
- `aicontext status`
- `aicontext diff`

`status` reports, per export target, how much content is always loaded versus loaded only by scope or relevance, and whether generated files are in sync with `.aicontext/`.

This section is informative only; command behavior is defined by tooling implementation, not by the file format itself.
