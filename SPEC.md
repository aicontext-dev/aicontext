# AIContext Specification

Status: DRAFT  
Version: 0.1.0-draft  
Last updated: 2026-11-01

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
```

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
    "targets": ["agents", "copilot", "claude", "cursor", "windsurf"],
    "defaultTarget": "all"
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
- generation timestamp
- tool target

### 6.1 Planned export targets

- `agents` -> `AGENTS.md`
- `copilot` -> `.github/copilot-instructions.md` (and optionally `.github/instructions/*.instructions.md`)
- `claude` -> `CLAUDE.md`
- `cursor` -> `.cursor/rules/*.mdc`
- `windsurf` -> `.windsurf/rules/*.md`
- `aider` -> `CONVENTIONS.md` (optional)

### 6.2 Deterministic output

For identical input and configuration, export output SHOULD be deterministic to minimize noisy diffs.

## 7. Validation levels

Validation is divided into three levels:

1. **Structural** (MUST): required files exist and are readable.
2. **Schema** (MUST): `context.json` is valid against declared schema.
3. **Quality** (SHOULD): recommended files are non-empty and materially useful.

Recommended-file absence is not a hard spec error in v0.1, but MUST be surfaced as warnings.

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

## 10. Security and privacy guidance

`.aicontext/` SHOULD NOT include:

- secrets (tokens, passwords, private keys)
- customer personal data
- confidential internal data not intended for repository readers

Implementations SHOULD provide lint checks for common secret patterns in context files.

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

This section is informative only; command behavior is defined by tooling implementation, not by the file format itself.
