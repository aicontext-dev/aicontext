# AIContext Specification

Status: DRAFT  
Version: 0.1.0-draft  
Last updated: 2026-11-12

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

Each rule is a markdown file directly inside `.aicontext/rules/`. The file name (without `.md`) is the rule name; it MUST consist of one or more lowercase letters, digits, `-` or `_` (in any order, so `_team` and `-team` are valid names), and MUST NOT be one of the reserved names `architecture`, `decisions`, `patterns`, `constraints`, `glossary`, `project`.

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
| `paths` | Path patterns (4.4.2): relative to the project root, with `/` separators. The rule applies when working with matching files. Accepts a list or a comma-separated string, and MUST contain at least one pattern. |
| `description` | One line explaining when the rule is relevant. MUST NOT be empty. |

The rule kind follows from the frontmatter:

- `paths` present: **scoped**, applies only to matching files.
- only `description` present: **on-demand**, loaded when the agent decides it is relevant.
- neither: **always on**.

A key that is present is not the same as a key that is absent. A rule whose `paths` or `description` is present but has no usable value (`paths: []`, `paths:`, `description:`) is invalid; an implementation MUST report an error and MUST NOT export the rule as always-on or in any other broader form than the author wrote.

Implementations MUST ignore unknown frontmatter keys, whatever their value looks like (including a nested value), and SHOULD warn about them. Keys are case-sensitive: `Paths` is an unknown key, not `paths`, so a rule that spells it that way is always on. Implementations SHOULD treat `architecture.md`, `decisions.md`, `patterns.md` and `glossary.md` as on-demand content and `README.md` and `constraints.md` as always-on content when they apply a split layout (see 6.4).

#### 4.4.1 Frontmatter syntax

The frontmatter is a small subset of YAML, not YAML: text that is valid YAML is not necessarily valid frontmatter, and the reverse. Implementations need no YAML parser, but MUST read the following the same way, because a different reading can change the scope of a rule.

- **Block.** The text is read after its line breaks (LF, CRLF or a lone CR, see 6.2) have become LF; the rules below are stated for text with LF. The frontmatter starts on the first line of the file, which is exactly `---`, and ends at the next line that is `---` (trailing whitespace ignored). Everything after that line is the body. A file whose first line is not `---` has no frontmatter. A block that is never closed is an error.
- **Lines.** Blank lines and lines whose first non-blank character is `#` are ignored. Every other line is either a *key line* or belongs to the value of the key above it. A key line starts in the first column and has the form `key: value`; the key is the text before the first colon, trimmed, and is not empty. A line that is indented or is a list item (`- item`) belongs to the key above it; such a line with no key above it, and any other line, is an error. A key MUST NOT appear twice, and repeating it is an error (the unknown keys too).
- **Scalars.** A scalar is plain text, or a quoted string: `"…"` (the escapes `\"` and `\\`, any other backslash is literal) or `'…'` (`''` stands for one quote). A quote starts a quoted string only at the start of a value or of a list item; anywhere else it is an ordinary character (`src/it's/**` is a plain item). A quoted string in the value of a known key MUST be closed, and only a comment may follow the closing quote; otherwise the value is an error. The value of an unknown key is not interpreted at all, so these rules do not apply to it.
- **Lists.** A flow list is `[item, item]`, a block list is one `- item` line per item below a key with no inline value. Commas inside `{ }` do not separate items. Items are trimmed. An item written as a quoted string is read without its quotes, so `["a, b", c]` has the items `a, b` and `c`.
- **Comments.** A `#` at the start of a value, or after whitespace, outside a quoted string, starts a comment that runs to the end of the line. A value that has to contain ` #` is quoted (`description: "Issue #12"`).
- **Nested values.** An indented or multi-line block under a key that is not a block list is a *nested value*. The subset does not interpret it: it is ignored for an unknown key, and an error for `paths` and `description`.

How the known keys read a value:

- `paths`: a flow list, a block list, or a scalar that is read as a comma-separated list. A scalar that is one quoted string is the text of the list (`paths: "src/**, lib/**"` has two items); several quoted items need the brackets (`["src/**", "lib/**"]`), and a bare list with a quoted item is an error. Empty items are ignored (`[a, , b]` and `a,,b` have the items `a` and `b`). At least one item MUST remain, otherwise the rule is invalid. Every item MUST be a pattern of 4.4.2, which includes using `/` as the separator; a rule with an item that is not is invalid.
- `description`: one scalar. A list or a nested value is an error, and so is an empty value.
- Any other key: not read, only warned about.

The [conformance suite](./conformance/README.md) lists rule files for these cases with the outcome an implementation has to produce.

#### 4.4.2 Path patterns

Tools read the same pattern text in different ways: `*.py` is the root-level files for one tool and the files at any depth for another, a comma separates patterns in some formats and is an ordinary character in others, and `{ts,tsx}` or `[id]` are syntax for some tools and plain text for others. A rule that has to apply to the same files in every tool needs one meaning that does not depend on the tool, and exporters translate it. This section defines that meaning for the items of `paths`: each item of the list, after the frontmatter was read as 4.4.1 says, so a comma inside an item is part of the pattern.

**Meaning.** A pattern is relative to the project root and has to match the whole path of a file, written with `/` separators. The path and the pattern are split into segments at `/`.

- A segment without `*` matches only a segment with the same text, and letter case is significant. A pattern without `/` is a single segment, so it matches root-level files only: `*.ts` matches `app.ts` and not `src/app.ts`, and `Makefile` matches the file `Makefile` and not `docs/Makefile`. Write `**/*.ts` for files at any depth.
- `*` inside a segment matches any run of characters, including none, but never a `/`. It also matches names that start with a dot: `*` matches `.env`.
- A segment that is exactly `**` matches directories. In the middle or at the start of a pattern it matches zero or more of them (`src/**/*.ts` matches `src/app.ts` and `src/a/b/app.ts`, and `**/*.ts` matches `app.ts`). As the last segment it matches one or more segments, that is everything below the directory in front of it (`src/**` matches `src/a.ts` and `src/a/b.ts`, and does not match a file called `src`). `**` MUST NOT be part of a longer segment (`a**b`), and `**` alone is not a pattern: write `**/*` for every file, or leave `paths` out to make the rule always on.
- `{a,b}` is a shorthand for separate patterns, one for each alternative: `src/**/*.{ts,tsx}` stands for `src/**/*.ts` and `src/**/*.tsx`. A pattern may have several groups, which are expanded in turn, and the expansion MUST NOT produce more than 64 patterns. A group has at least two alternatives, none of them empty or with a space around it, and groups MUST NOT be nested.
- Every other character stands for itself. In particular a comma is an ordinary character in an item that came from a list: `["src/a,b/**"]` is one pattern, which matches `src/a,b/x.ts`. An unquoted comma-separated value is split into items first (4.4.1).

**Reserved syntax.** A pattern MUST NOT contain `?`, `[`, `]`, `(`, `)`, `!` or `\`, MUST NOT start or end with `/`, and MUST NOT have an empty segment (`a//b`) or a `.` or `..` segment. Tools read these as glob syntax or as paths in different ways, and this version defines no way to write them literally, so they are not patterns. Use `*` for the part of a name that has to stay open. A rule with an item that is not a pattern is invalid (4.4): an implementation MUST report an error and MUST NOT export the rule, in particular not as an always-on rule and not with the item changed.

The [conformance suite](./conformance/README.md) lists a fixed set of file paths and, for each supported pattern, exactly the files it matches, and the items that are not patterns.

**Translation.** An exporter MUST write a pattern in a tool's own format only in a form that the tool is documented to read as this section defines it. Where the tool reads the same text differently, the exporter writes the form that means the same (Claude reads `*.md` at any depth, so a root-level pattern is written `/*.md`), and a brace group is expanded before anything is written, so no tool has to know about it. Where no such form is documented (a tool whose list is one comma-separated text cannot carry a comma, and a tool whose documentation shows no more than one pattern cannot carry several), the export MUST be refused with an error that names the rule and the pattern, and nothing is written. The exporter MUST NOT write the pattern as it is, and MUST NOT move the rule into a file that applies to the whole project. This differs from the fallbacks of 6.4: those keep content for which a tool has no mechanism (on-demand content, or scopes in a tool without scoping) and state the scope in text, while a tool that supports scopes is never given a rule with another scope than its author wrote. What each tool is documented to read is recorded in the user manual; it was checked against the vendors' documentation and not by running the tools.

**Limits.** None of the tools the reference implementation exports to documents whether `*` and `**` match names that start with a dot or whether matching is case-sensitive, and a file system without case (the default on Windows and macOS) can make a tool match `src/Foo` with `src/foo`. This section defines both without checking them against the tools, so a pattern that has to reach a hidden directory SHOULD name it (`.github/**`).

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

### 5.1 Properties

`context.json` MUST be a JSON object. The properties below are the only ones allowed (in 0.1.0 an unknown property, at any level, is an error; this protects against typos). Unless stated otherwise a string MUST NOT be empty.

| Property | Type | Required | Rule |
|----------|------|----------|------|
| `$schema` | string (URI) | no | SHOULD be the published schema URL; implementations SHOULD warn when it is missing or different |
| `specVersion` | string | **yes** | MUST be a specification version this document defines; `0.1.0` is the only one in this version |
| `project.name` | string | **yes** | MUST contain at least one character that is not whitespace |
| `project.description` | string | no | |
| `project.primaryLanguage` | string | no | |
| `project.stack` | array of strings | no | entries MUST be unique |
| `exports.targets` | array of strings | no | at least one entry; entries MUST be unique and MUST be target names from 6.1 |
| `exports.defaultTarget` | string | no | MUST be `all` or a target name from 6.1; when `exports.targets` is present it MUST also be `all` or one of its entries |
| `exports.layout` | string | no | `single` or `split` (6.4) |
| `exports.nestedAgents` | boolean | no | see 6.4 |
| `metadata.lastReviewed` | string | no | a calendar date in the form `YYYY-MM-DD` (an RFC 3339 `full-date`); `2026-02-30` is not a date |

`project`, `exports` and `metadata` are objects; `project` is required.

### 5.2 Relation to the JSON Schema

The [published schema](./schema/v1/context.schema.json) expresses the structure in 5.1 and is the normative form of it. Two rules need more than a schema evaluator does by default, and an implementation MUST enforce them in addition (section 7):

1. `exports.defaultTarget` is listed in `exports.targets` (or is `all`). JSON Schema cannot compare a value with another property's entries.
2. `metadata.lastReviewed` is a date. The schema declares `"format": "date"`, which JSON Schema treats as an annotation unless the evaluator is told to assert formats; the implementation MUST turn format assertion on, or check the date itself.

The [conformance suite](./conformance/README.md) checks the rules in 5.1 and 5.2 for `context.json`, and the rule files of 4.4 and 4.4.1 and the path patterns of 4.4.2: every case names the section it relies on.

## 6. Export model

`.aicontext/` is canonical. Generated tool files are derived artifacts.

Implementations MUST treat exports as generated output and SHOULD include a generated-file header with:

- source path (`.aicontext/`)
- a hash of the source content (RECOMMENDED, e.g. `Source-hash: sha256:<hex>`), which identifies the input without breaking determinism
- tool target

Implementations SHOULD NOT include a generation timestamp, because it makes every run produce a different file (see 6.2). A timestamp MAY be included only when the implementation offers it as an explicit opt-in.

### 6.1 Export targets

An implementation that exports without an explicitly requested target (for example a CLI invoked without `--target`) SHOULD use `exports.defaultTarget`. An explicitly requested target always takes precedence. `defaultTarget: "all"` means the targets listed in `exports.targets` (or the implementation's default set when `exports.targets` is not set); any other value means that single target only. When neither is available the implementation SHOULD fail with a message that names both options, and SHOULD report that the value came from `context.json`.

- `agents` -> `AGENTS.md`
- `copilot` -> `.github/copilot-instructions.md` (and `.github/instructions/*.instructions.md` in the split layout)
- `claude` -> `CLAUDE.md` (and `.claude/rules/*.md` in the split layout)
- `cursor` -> `.cursor/rules/*.mdc`
- `windsurf` -> `.windsurf/rules/*.md`
- `devin` -> `.devin/rules/*.md`. Devin also loads `.windsurf/rules`, so enabling both `windsurf` and `devin` loads the same rules twice; implementations SHOULD warn about this and SHOULD NOT include `devin` in a default "all targets" set.
- `aider` -> `CONVENTIONS.md` (optional)

### 6.2 Deterministic output

For identical input and configuration, export output SHOULD be deterministic to minimize noisy diffs. In particular, output SHOULD NOT depend on the current time, the host operating system, or line-ending settings (LF is RECOMMENDED), and implementations SHOULD NOT rewrite files whose content would not change.

A line break in a source is LF, CRLF or a lone CR; U+0085, U+2028 and U+2029 are ordinary characters. An implementation SHOULD turn every source text (the Markdown files, the rule files and the text taken from `context.json`) into LF before it parses, hashes, scans or writes it, so that the same text written with LF, CRLF, CR or a mix of them gives the same rules, the same source hash, the same line numbers in diagnostics and the same generated files, which contain LF only. This matters for more than tidiness: a rule file whose header is not found because of its line endings would silently become a broader rule (4.4). A generated file that differs from the planned content only by its line endings is up to date.

### 6.3 Safe overwrite

Implementations MUST NOT silently overwrite an existing file that was not generated by the implementation (a file without the generated-file header). They SHOULD refuse and require an explicit user override. Implementations SHOULD offer a dry-run mode and a check mode that reports out-of-date exports (for CI).

Implementations MUST NOT write or delete a file outside the project root. A symbolic link or directory junction inside the project can point elsewhere, so the real location of every path to be written or deleted (links resolved) MUST be inside the real project root before anything is changed. Links that stay inside the project MAY be used. When a path is refused for this reason, implementations SHOULD write nothing and fail, as they do for an overwrite conflict. An implementation that plans first and writes later SHOULD verify again before writing that the paths and files are still as planned, and refuse a plan that is out of date.

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

Content is never dropped: where a tool has no mechanism for scoping (Agents and Aider, and every tool in the single layout), the content is kept in the main file, with the scope stated in text (for example `Applies to: src/ui/**`). A tool that does support scopes is treated differently: it never gets a rule with another scope than its author wrote. When a pattern cannot be written in such a tool's own format with the meaning of 4.4.2, the export is refused instead (4.4.2, Translation), and the rule is not moved into the main file.

The description of a scoped rule is not a second way to load it. Where a tool documents that a description can load a rule independently of its path scope, an exporter MUST NOT write the description of a scoped rule as a field of that tool's format, and SHOULD keep it as text in the body, so it is not lost. This applies to Copilot: in the VS Code Local harness a `description` can load an instructions file by relevance, separately from `applyTo` and not as a condition on it (checked against the vendor documentation on 2026-11-11, not by running the tool), so a scoped Copilot file has `applyTo` alone and a `Description:` line in its body. The documentation of Cursor, Windsurf and Devin does not show such an independent trigger for a rule that has a path scope, so they keep the description field; that is not a statement that they have none. On-demand content has no path scope to widen and keeps its description as before.

#### Nested `AGENTS.md` (optional)

`AGENTS.md` has no scoping of its own, but tools that read it also read an `AGENTS.md` located in a subdirectory when working there. With `exports.nestedAgents: true` and the split layout, an implementation MAY write directory-scoped rules to `<directory>/AGENTS.md` instead of the root file. This applies only to rules whose every pattern is an exact directory scope (`<dir>/**` or `<dir>/**/*`, the two spellings of everything below `<dir>`; `<dir>` names a file and `<dir>/` is not a pattern, see 4.4.2) and whose directories exist; all other rules stay in the root file. Implementations MUST NOT create directories for this purpose and SHOULD report which rules were kept in the root file and why. A directory that contains its own `.aicontext/` is a separate project: an implementation MUST NOT write nested files into it, map rules to it or remove files from it when exporting the enclosing project, including when it is reached through a symbolic link or directory junction. Orphaned generated nested files MAY be removed using the same header rule as other generated files, outside separate projects.

Because several tools read both `AGENTS.md` and their own rule files, enabling `nestedAgents` together with other scoping targets (for example `cursor` or `copilot`) can make a tool see the same rule twice.

Generated rule files MUST be named with the `aicontext-` prefix, so they never collide with hand-written rule files, and MUST carry the generated-file header. When a generated rule file no longer corresponds to a source, implementations SHOULD remove it, and MUST NOT remove files that lack the prefix or the header.

## 7. Validation levels

Validation is divided into three levels:

1. **Structural** (MUST): required files exist and are readable.
2. **Schema** (MUST): `context.json` is valid against declared schema and satisfies the rules in 5.1 and 5.2.
3. **Quality** (SHOULD): recommended files are non-empty and materially useful.

Recommended-file absence is not a hard spec error in v0.1, but MUST be surfaced as warnings. Implementations SHOULD also warn about recommended files that exist but are empty. Implementations MAY report obvious placeholder text in context files (for example `TODO: describe ...` or `**Tech stack:** TODO`) as warnings, so that a project made of starter stubs is not mistaken for useful content. Such a check is a heuristic and cannot tell whether the remaining content is useful or up to date.

Schema validation MUST use the published schema for the declared `specVersion`. Implementations MAY embed a copy of the schema instead of fetching it.

Export SHOULD apply the same checks: an implementation SHOULD NOT generate files from a project that has validation errors at the structural or schema level (including an unsupported `specVersion`). Warnings, such as missing recommended files or possible secrets, do not block an export; whether warnings fail a build is a separate choice (for example a strict mode of the validator).

## 8. Compliance profiles

### 8.1 Core profile (minimum compliant)

A repository is Core-compliant if:

- `.aicontext/README.md` exists and is non-empty
- `.aicontext/context.json` exists and passes schema validation (7)

### 8.2 Extended profile

A repository is Extended-compliant if Core profile is met and all recommended files exist and are non-empty:

- `architecture.md`
- `decisions.md`
- `constraints.md`

The profiles describe structure only. A repository can meet the Extended profile and still contain starter text or outdated content; whether the content is useful is a quality matter (section 7).

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

While the specification is a draft (`0.x`), additions may be made within the current version and `specVersion` stays `0.1.0`. The first change that makes existing files invalid or changes their meaning bumps the specification version, and the schema is widened so that files declaring older supported versions remain valid. This concerns `context.json`, which declares the version. Rule files (4.4) carry no version of their own and are the part of the draft that is still being settled, so a change of what a rule file means is recorded in the table below and in the CHANGELOG, with migration examples, and does not bump `specVersion`.

| Tool version | Specification version | Format changes introduced |
|--------------|-----------------------|---------------------------|
| 0.1.0 – 0.2.0 | 0.1.0-draft | Initial format |
| 0.3.0 | 0.1.0-draft | Rules (4.4), safe overwrite (6.3), layouts and nested `AGENTS.md` (6.4), `devin` target |
| 0.4.0 | 0.1.0-draft | None; `exports.defaultTarget` semantics clarified (6.1) |
| 0.5.0 | 0.1.0-draft | None; requirements clarified: no writes outside the project and separate projects (6.3, 6.4), export applies validation errors (7), the secret lint is a heuristic (10) |
| 0.6.0 | 0.1.0-draft | None; requirements clarified: the properties of `context.json` and the rules beyond the schema (5.1, 5.2; the schema now also states that `project.name` has a non-whitespace character), a plan is verified again before writing (6.3), separate projects also behind links (6.4). No document that was valid before is rejected |
| 0.7.0 | 0.1.0-draft | `context.json` and its schema do not change. Requirement clarified: a source outside the project root SHOULD NOT be read (10). Rule files (4.4, 4.4.1, 4.4.2): the frontmatter is a strict subset with one reading (4.4.1), and the items of `paths` have one meaning in every tool (4.4.2), with a refused export instead of a changed scope (6.4). Rule files that were accepted before can be invalid now (an empty `paths` or `description`, a repeated key, a bad quote, a pattern outside 4.4.2), and a pattern without `/` means the root only for Claude too. The generated Copilot file of a scoped rule no longer has a `description` field (6.4). A lone CR is a line break like LF and CRLF (6.2), so a rule file with CR endings is read as written instead of losing its header. See the CHANGELOG for the migration |

## 10. Security and privacy guidance

`.aicontext/` SHOULD NOT include:

- secrets (tokens, passwords, private keys)
- customer personal data
- confidential internal data not intended for repository readers

Implementations SHOULD provide lint checks for common secret patterns in context files. Such findings SHOULD be reported as warnings (and MAY be promoted to errors by an explicit strict mode), SHOULD NOT print the matched secret value, and SHOULD offer a way to mark intentional examples.

Such a lint is a heuristic aid: it does not find every secret and is no guarantee that none is present, and it does not replace dedicated secret-scanning tools. Implementations SHOULD document which files they scan, and SHOULD report a file they could not scan completely (for example an unreadable file) instead of skipping it silently.

Implementations read the files of `.aicontext/` with the permissions of the user who runs them, and a repository can contain symbolic links, so a source can lead to a file anywhere on the machine, and its content would end up in the generated files. Implementations SHOULD NOT read a source whose real location (links resolved) is outside the project root, and SHOULD report it as an error instead of using its content. Links that stay inside the project MAY be used. Messages that repeat text taken from a file (a line, a key, a path) SHOULD NOT repeat text that looks like a secret.

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

Independent implementations can check their handling of `context.json` and of rule files against the language-neutral [conformance suite](./conformance/README.md), which lists valid and invalid documents, rule files and path patterns and what an implementation has to produce.
