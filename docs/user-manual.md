# AIContext – User Manual

> Applies to version `0.3.0`.

---

## Contents

1. [What AIContext is and why use it](#1-what-aicontext-is-and-why-use-it)
2. [Quick start (5 minutes)](#2-quick-start-5-minutes)
3. [What is in the `.aicontext/` directory](#3-what-is-in-the-aicontext-directory)
4. [`context.json` and the schema](#4-contextjson-and-the-schema)
5. [CLI – all commands](#5-cli--all-commands)
6. [Exports – what is generated and how to work with it](#6-exports--what-is-generated-and-how-to-work-with-it)
7. [Rules and the `split` layout (for larger projects)](#7-rules-and-the-split-layout-for-larger-projects)
8. [Nested `AGENTS.md` (monorepos)](#8-nested-agentsmd-monorepos)
9. [Validation in detail](#9-validation-in-detail)
10. [Using it in CI](#10-using-it-in-ci)
11. [Recipes: typical situations](#11-recipes-typical-situations)
12. [Troubleshooting](#12-troubleshooting)
13. [How to write good context](#13-how-to-write-good-context)
14. [Glossary](#14-glossary)

---

## 1. What AIContext is and why use it

AI coding assistants (GitHub Copilot, Cursor, Claude Code, Windsurf, Aider, …) need to know **how your project works**: what its architecture is, what must not be done, which conventions apply. Each tool uses **its own file** for this:

| Tool | File |
|---|---|
| GitHub Copilot | `.github/copilot-instructions.md` |
| Claude Code | `CLAUDE.md` |
| Cursor | `.cursor/rules/*.mdc` |
| Windsurf | `.windsurf/rules/*.md` |
| Aider | `CONVENTIONS.md` |
| Many others (Codex, Copilot, …) | `AGENTS.md` |

If a team uses several tools, the same information is written and maintained several times, and the copies drift apart over time.

**AIContext solves this:** you write the information **once** into the `.aicontext/` directory (plain markdown), and the `aicontext export` command generates the files for all tools from it.

```
.aicontext/   (written by you, single source of truth)
     │
     │  aicontext export
     ▼
AGENTS.md   CLAUDE.md   .github/copilot-instructions.md   .cursor/rules/…   …   (generated)
```

What else you get:

- **Checks** (`validate`): missing files, configuration errors, accidentally pasted passwords and keys.
- **Context savings**: large projects can split their context so that it is not all loaded on every question (chapter 7).
- **Overview** (`status`): how much context each tool loads and whether the exports are up to date.

What AIContext is **not**: it does not replace documentation or source code. It is a short, human-readable "project guide" for AI.

---

## 2. Quick start (5 minutes)

### Step 0: Prerequisites

You have two installation options:

- **Standalone binary (recommended, no .NET needed)** – download from a GitHub Release, see Step 1.
- **.NET tool** – requires the **.NET SDK 9 or 10**. Check with `dotnet --version`.

The language or project type of your repository does not matter – AIContext works with any project (Python, JavaScript, Go, …).

### Step 1: Installation

**A) Standalone binary (no .NET)**

On the [Releases](https://github.com/aicontext-dev/aicontext/releases/latest) page, download the archive for your system:

| System | File |
|---|---|
| Windows x64 | `aicontext-win-x64.zip` |
| Linux x64 | `aicontext-linux-x64.tar.gz` |
| Linux arm64 | `aicontext-linux-arm64.tar.gz` |
| macOS Intel | `aicontext-osx-x64.tar.gz` |
| macOS Apple Silicon | `aicontext-osx-arm64.tar.gz` |

Extract it and put `aicontext` (`aicontext.exe` on Windows) into a directory on your `PATH`. You can verify the file against `SHA256SUMS.txt` from the same page.

The binaries are not code-signed yet:
- **macOS:** if Gatekeeper blocks a file downloaded through a browser, run `xattr -d com.apple.quarantine aicontext`.
- **Windows:** SmartScreen may show a warning – More info → Run anyway.

```powershell
aicontext --help
```

**B) .NET tool**

```powershell
dotnet tool install --global AIContext.CLI
aicontext --help
```

To update later: `dotnet tool update --global AIContext.CLI`. To update the binary, download the new version from Releases.

### Step 2: Create the skeleton

In the root directory of your project:

```powershell
aicontext init --name my-project
```

Output:

```
Created: C:\projects\my-project\.aicontext\README.md
Created: C:\projects\my-project\.aicontext\context.json
```

Two files are created. If you omit `--name`, the directory name is used.

### Step 3: Write a short project description

Open `.aicontext/README.md` and replace `TODO`:

```markdown
# Project: my-project

Online shop selling coffee. Backend in C#, frontend in React.

**Tech stack:** .NET 10, EF Core, React 18, PostgreSQL  
**Architecture:** Vertical Slice Architecture  
**Testing:** xUnit + Playwright
```

The first `# …` line is omitted on export (the heading of generated files comes from `project.name`); the rest ends up in all generated files.

### Step 4: Check

```powershell
aicontext validate
```

Output for a fresh project:

```
WARNING: Recommended file missing: .aicontext/architecture.md.
WARNING: Recommended file missing: .aicontext/decisions.md.
WARNING: Recommended file missing: .aicontext/constraints.md.
AIContext validation passed (Core profile).
```

It passed (Core profile), but the tool recommends adding three files (chapter 3). You do not have to yet.

### Step 5: Export

```powershell
aicontext export --target all
```

Output:

```
Created (agents): C:\projects\my-project\AGENTS.md
Created (copilot): C:\projects\my-project\.github\copilot-instructions.md
Created (claude): C:\projects\my-project\CLAUDE.md
Created (cursor): C:\projects\my-project\.cursor\rules\project.mdc
Created (windsurf): C:\projects\my-project\.windsurf\rules\project.md
Created (aider): C:\projects\my-project\CONVENTIONS.md
Export completed (6 changed, 0 unchanged).
```

Done. Your AI assistants now know your project.

### Step 6: What to commit

**Everything** belongs in git: the `.aicontext/` directory and the generated files (assistants read them directly from the repository). **Do not edit** generated files by hand – the next export would overwrite them. Edit `.aicontext/` and run `aicontext export` again.

> Tip: if you only change `.aicontext/`, just run `aicontext export --target all` again. Unchanged files are not rewritten, so `git diff` shows only real changes.

---

## 3. What is in the `.aicontext/` directory

```text
.aicontext/
  README.md          required – short project description
  context.json       required – settings (name, targets, …)
  architecture.md    recommended – why the project is built the way it is
  decisions.md       recommended – important decisions and their reasons
  constraints.md     recommended – hard rules ("never do X")
  patterns.md        optional – code patterns you want repeated
  glossary.md        optional – domain terms
  rules/             optional – rules for only part of the project (chapter 7)
    ui.md
    connectors.md
```

### What to write where

**`README.md`** (required) – 5 to 10 lines: what the project does, the main stack, the architecture style, how it is tested.

**`architecture.md`** – not just "what the modules are", but **why**:

```markdown
The application is split into vertical slices (Features/Orders, Features/Catalog).
Each slice has its own endpoints, validation and database access.
Reason: the team works on features in parallel and we want to minimize conflicts.
Modules must not call each other directly – they communicate via events (Azure Service Bus).
```

**`decisions.md`** – decisions in the form "context → decision → consequence":

```markdown
### D1. PostgreSQL instead of SQL Server
Context: we host on a cheap Linux VPS.
Decision: PostgreSQL + EF Core.
Consequence: we do not use T-SQL specifics; migrations are done through EF.
```

**`constraints.md`** – short hard rules. This is the most valuable file, because it prevents recurring AI mistakes:

```markdown
- Never write SQL directly in controllers; use repositories.
- Never add a dependency without approval in a PR.
- Store all dates in UTC.
- Do not edit files in the `Migrations/` folder by hand.
```

**`patterns.md`** – "this is how we do it" (for example what a handler looks like, how tests are written).

**`glossary.md`** – terms that AI often gets wrong:

```markdown
- **Order** – a customer's order (not a shopping cart).
- **Slice** – one application feature including its endpoints and data.
```

### Core and Extended profile

- **Core profile:** a non-empty `README.md` and a valid `context.json` exist.
- **Extended profile:** Core + non-empty `architecture.md`, `decisions.md`, `constraints.md`.

At the end, `aicontext validate` prints which profile you meet. Extended is not required, but it helps AI assistants considerably.

---

## 4. `context.json` and the schema

`context.json` contains machine-readable settings. The smallest valid file:

```json
{
  "$schema": "https://aicontext.dev/schema/v1/context.schema.json",
  "specVersion": "0.1.0",
  "project": {
    "name": "my-project"
  }
}
```

Extended example:

```json
{
  "$schema": "https://aicontext.dev/schema/v1/context.schema.json",
  "specVersion": "0.1.0",
  "project": {
    "name": "my-project",
    "description": "Online shop selling coffee",
    "primaryLanguage": "C#",
    "stack": [".NET 10", "EF Core", "React", "PostgreSQL"]
  },
  "exports": {
    "targets": ["agents", "copilot", "claude", "cursor"],
    "defaultTarget": "all",
    "layout": "single",
    "nestedAgents": false
  },
  "metadata": {
    "lastReviewed": "2026-11-02"
  }
}
```

### All fields

| Field | Required | Meaning |
|---|---|---|
| `$schema` | no (recommended) | schema URL; your editor can use it for completion and checking |
| `specVersion` | **yes** | format version; currently always `"0.1.0"` (this is the version of the format, not of the tool) |
| `project.name` | **yes** | project name (non-empty) |
| `project.description` | no | one-line description |
| `project.primaryLanguage` | no | main language |
| `project.stack` | no | list of technologies (no duplicates) |
| `exports.targets` | no | which tools are generated for with `--target all` |
| `exports.defaultTarget` | no | `all` or one of `targets` (currently informational and validated only; `export` always requires `--target`) |
| `exports.layout` | no | `single` (default) or `split` (chapter 7) |
| `exports.nestedAgents` | no | `true` = nested `AGENTS.md` (chapter 8; only with `split`) |
| `metadata.lastReviewed` | no | date the content was last reviewed, `YYYY-MM-DD` |

Allowed target values: `agents`, `copilot`, `claude`, `cursor`, `windsurf`, `devin`, `aider`.

Unknown fields are not allowed (the tool reports them as errors) – this protects against typos.

### Schema in the editor

Thanks to the `"$schema"` line, **VS Code**, for example, automatically offers the allowed keys and values while you edit `context.json` and underlines errors. The schema is public at  
`https://aicontext.dev/schema/v1/context.schema.json`.

The `aicontext` tool has the same schema built in, so `validate` works offline too.

### Example error

```json
{ "specVersion": "0.2.0", "project": { "name": "x", "bogus": 1 } }
```

```
ERROR: context.json /project/bogus: unknown property is not allowed.
ERROR: context.json /specVersion: The string value is not a match for the indicated regular expression
Validation failed with 2 error(s).
```

(Warnings, such as missing recommended files, may also be printed before the errors.)

An error always starts with the path to the problem (`/project/bogus`), so you know exactly what to fix.

---

## 5. CLI – all commands

All commands have an optional `--path <directory>` (the current directory by default). `aicontext --help` prints an overview.

### `aicontext init`

```powershell
aicontext init [--path <directory>] [--name <name>] [--profile core|extended] [--force]
aicontext --version
```

Creates `.aicontext/README.md` and `.aicontext/context.json` (Core profile, the default). With `--profile extended` it also creates starter `architecture.md`, `decisions.md` and `constraints.md` (replace the `TODO` markers with real content), so `validate` reports the Extended profile. If any of the files already exist, it fails and writes nothing; `--force` overwrites them. `aicontext --version` prints the tool version and the specification version.

### `aicontext validate`

```powershell
aicontext validate [--path <directory>] [--strict]
```

Checks `.aicontext/`:

1. required files and the validity of `context.json` against the schema,
2. recommended files (a warning if missing or empty),
3. rules in `rules/`,
4. accidentally pasted secrets (a warning),
5. prints the profile (Core/Extended).

Exit codes: `0` = OK, `1` = error. With `--strict`, warnings count as errors (useful in CI).

### `aicontext export`

```powershell
aicontext export --target <target> [--path <directory>] [--layout single|split] [--force] [--dry-run] [--check]
```

`<target>` is `agents`, `copilot`, `claude`, `cursor`, `windsurf`, `devin`, `aider` or `all`.

| Option | Meaning |
|---|---|
| `--target all` | all targets from `exports.targets`; if not set, all except `devin` |
| `--layout split` | split layout (chapter 7); overrides the value from `context.json` |
| `--dry-run` | only shows what would happen; writes nothing |
| `--check` | like `--dry-run`, but returns exit code `1` if anything is out of date or missing (for CI) |
| `--force` | also overwrites files that were not generated by aicontext |

Important safety rules:

- Only a file that has the header `Auto-generated by aicontext export` is overwritten. A hand-written `AGENTS.md` will **never** disappear by accident.
- If an export would overwrite a foreign file, **nothing is written** (not even the other files) and the command fails.
- Unchanged files are not written; the output is always the same (no date, same line endings), so in git you only see real changes.

Examples:

```powershell
aicontext export --target all                # everything
aicontext export --target cursor             # Cursor only
aicontext export --target all --dry-run      # preview
aicontext export --target all --check        # check whether the export is up to date
```

### `aicontext status`

```powershell
aicontext status [--path <directory>] [--layout single|split]
```

Shows how much context each tool loads and whether the files are up to date:

```
Project: aicontext
Layout:  split
Rules:   1
  tests            scoped    tests/**

Target    Always on             Scoped / on-demand    Files  State
agents    10.1 KB (~2,588 tok)  -                     1      in sync
copilot    9.7 KB (~2,488 tok)   0.6 KB (~159 tok)    2      in sync
cursor     2.3 KB (~601 tok)     9.0 KB (~2,298 tok)  6      in sync
windsurf   2.3 KB (~593 tok)     9.0 KB (~2,305 tok)  6      in sync
```

- **Always on** – loaded with every request.
- **Scoped / on-demand** – loaded only when working with the matching files, or when the assistant considers it necessary.
- **Tokens** are only an estimate (~4 characters per token).
- **State**: `in sync` (up to date), `out of date (run export)`, `not exported`, `conflict`.

With the `single` layout it also prints a tip about how much `split` would reduce the always-on context.

---

## 6. Exports – what is generated and how to work with it

### Targets table

| Target | Main file | Note |
|---|---|---|
| `agents` | `AGENTS.md` | universal format, read by many tools |
| `copilot` | `.github/copilot-instructions.md` | GitHub Copilot |
| `claude` | `CLAUDE.md` | Claude Code |
| `cursor` | `.cursor/rules/project.mdc` | Cursor ("always on" rule) |
| `windsurf` | `.windsurf/rules/project.md` | Windsurf |
| `devin` | `.devin/rules/project.md` | Devin; **not in the default `all`**, because it also reads `.windsurf/rules` |
| `aider` | `CONVENTIONS.md` | Aider |

### Which targets to enable?

Enable only the tools your team actually uses, in `context.json`:

```json
"exports": { "targets": ["agents", "copilot", "cursor"] }
```

Beware of overlap: several tools read `AGENTS.md` **in addition** to their own files (for example Copilot, Cursor, Devin). If you export `agents` together with their own targets, they may see the same content twice. If that bothers you, keep `agents` only for tools that have no other format.

### What a generated file looks like

```markdown
<!-- Auto-generated by aicontext export --target agents -->
<!-- Source: .aicontext/ | Source-hash: sha256:232ffd488dabfb2c | Do not edit directly -->

# my-project

Online shop selling coffee. …

## Architecture
…
## Constraints
…
```

- The header says the file is generated (and this is how export knows it may overwrite it).
- `Source-hash` changes only when the content in `.aicontext/` changes – no date, no "noise" in git.

### Conflict with a hand-written file

If you already have your own `AGENTS.md`:

```
Conflict (agents): C:\projects\my-project\AGENTS.md
Would update (copilot): …
ERROR: C:\projects\my-project\AGENTS.md exists and was not generated by aicontext. Use --force to overwrite.
No files were written.
```

Solution: move its content into `.aicontext/` (README, constraints, rules) and only then `export --force`, or leave the `agents` target out of `targets`.

### Keeping exports up to date

Golden rule: **change in `.aicontext/` → `aicontext export --target all` → commit both**.  
To check that you did not forget: `aicontext export --target all --check` (or in CI, chapter 10).

---

## 7. Rules and the `split` layout (for larger projects)

### The problem

In the default layout (`single`), **everything is in one file per tool** and the assistant loads it on every question. In a large project this needlessly takes up the model's context window – for example, UI rules are not needed while editing the database layer.

### The solution: scoped rules

You put separate markdown files into `.aicontext/rules/`, each with a header (frontmatter) that says **when** the rule applies.

`.aicontext/rules/ui.md`:

```markdown
---
description: Conventions for React components
paths: ["src/ui/**", "**/*.tsx"]
---

- Use components from `src/ui/shared`; do not create your own buttons.
- Styles only through design tokens, no hard-coded colors.
```

`.aicontext/rules/connectors.md`:

```markdown
---
description: Rules for integration connectors to external APIs
paths: ["src/connectors/**"]
---

- Every external API call has a timeout and retry through `ConnectorPolicy`.
- Never pass external API errors to client responses in raw form.
```

### Three kinds of rules

| What you put in the header | Kind | When it is loaded |
|---|---|---|
| `paths` | **scoped** | when the assistant works with matching files |
| only `description` | **on-demand** | when the assistant decides it is relevant |
| nothing (no header) | **always on** | always |

`paths` can be a list or comma-separated text (`paths: src/ui/**, **/*.tsx`). Use `/` as the separator, also on Windows.

File name rules: lowercase letters, digits, `-`, `_` (for example `ui.md`, `api-v2.md`). Reserved names: `architecture`, `decisions`, `patterns`, `constraints`, `glossary`, `project`.

### Enabling it

In `context.json`:

```json
"exports": { "layout": "split" }
```

or one-off `aicontext export --target all --layout split`.

### What happens with `split`

- `README.md` and `constraints.md` (and rules without a scope) stay **always on** in a small main file.
- `architecture.md`, `decisions.md`, `patterns.md`, `glossary.md` become **on-demand**.
- Scoped and on-demand content goes to separate files with the `aicontext-` prefix:

| Tool | Scoped rules | On-demand content |
|---|---|---|
| Cursor | `.cursor/rules/aicontext-<name>.mdc` (`globs`) | separate `.mdc` with `description` |
| Copilot | `.github/instructions/aicontext-<name>.instructions.md` (`applyTo`) | stays in the main file |
| Claude Code | `.claude/rules/aicontext-<name>.md` (`paths`) | stays in the main file |
| Windsurf / Devin | `aicontext-<name>.md` (`trigger: glob`) | `trigger: model_decision` |
| `agents`, `aider` | cannot split content – rules are sections in a single file with the text "Applies to: …" | – |

**No content is lost:** where a tool does not know scopes, the text stays in the main file.

### Savings

`aicontext status` shows the difference. In this project the "always on" content in Cursor dropped from about 10 KB to 2.3 KB.

### What export does with `aicontext-*` files

- It creates and updates them.
- If you delete a rule or go back to `single`, the generated files are **removed**.
- Your own files (without the `aicontext-` prefix or without the generator header) are **never deleted or changed**.

---

## 8. Nested `AGENTS.md` (monorepos)

`AGENTS.md` has no scopes, but many tools also read an `AGENTS.md` in a subdirectory when the assistant works there. With the setting

```json
"exports": { "layout": "split", "nestedAgents": true }
```

rules bound to **exactly one directory** are written to `<directory>/AGENTS.md` instead of the root file.

Example: a rule with `paths: services/billing/**` → file `services/billing/AGENTS.md`.

Mapping rules (intentionally strict, so a rule never affects more or fewer files than you intended):

- Works: `dir/**`, `dir/**/*`, `dir/`, `dir` – and the directory must exist. Directories are never created.
- Does not work (the rule stays in the root `AGENTS.md`): `**/*.ts`, `src/ui/**/*.tsx`, a non-existent directory, a file path, a path outside the repository.
- If a rule has several patterns and even one cannot be mapped, the whole rule stays in the root.

`aicontext status` lists what went where and why some rule stayed in the root:

```
Nested AGENTS.md:
  services/billing/AGENTS.md  <- billing
  kept in root AGENTS.md: ts (pattern '**/*.ts' is not a directory scope (use '<directory>/**'))
```

> Note: if you also export Cursor/Copilot, they read `AGENTS.md` as well as their own files – they may see the same rule twice. Use `nestedAgents` mainly when you target tools that read only `AGENTS.md`.

---

## 9. Validation in detail

### Errors and warnings

| Kind | Examples | Consequence |
|---|---|---|
| **Error** | missing `README.md`/`context.json`, invalid JSON, schema violation, invalid rule (bad name, unclosed header) | `validate` exits with code `1` |
| **Warning** | missing/empty recommended file, possible secret, unknown key in a rule header, `windsurf` + `devin` together, `nestedAgents` without `split` | with `--strict` also code `1` |

### Protection against secrets

`validate` scans **all** `.md`, `.json`, `.yml`, `.yaml` and `.txt` files in `.aicontext/` (including `rules/`) and looks for:

- private keys (`-----BEGIN … PRIVATE KEY-----`),
- AWS access keys, GitHub tokens, Slack tokens, Google API keys, Stripe live keys, JWTs,
- hard-coded passwords and keys (`password = …`, `api_key: …`),
- credentials embedded in URLs (for example `scheme://user:password@host/db`).

Example:

```
WARNING: Possible secret (hard-coded credential) in .aicontext/README.md:3. Remove it, or add 'aicontext:allow-secret' to the line if it is an intentional example.
```

- The secret value is **never printed** – only the file and line.
- Placeholders (`<your-key>`, `${TOKEN}`, `changeme`) and ordinary text ("password: required") are ignored.
- If it is an intentional example, add `aicontext:allow-secret` on the same line.
- It is a **heuristic**: it does not find everything and is sometimes wrong. It does not replace a dedicated secret-scanning tool.

What `.aicontext/` should never contain: tokens, passwords, private keys, customer personal data, confidential information that repository readers should not see. The files end up in git and in AI tools.

---

## 10. Using it in CI

### GitHub Actions

```yaml
name: AIContext
on: [pull_request]

jobs:
  aicontext:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          dotnet-version: "10.0.x"
      - run: dotnet tool install --global AIContext.CLI
      - run: aicontext validate --strict
      - run: aicontext export --target all --check
```

- `validate --strict` fails on an error or warning (missing file, possible secret).
- `export --check` fails if someone changed `.aicontext/` and forgot to run `export`, or edited a generated file by hand.
- If the `aicontext` command is not found after installation, use `dotnet tool install AIContext.CLI --tool-path ./tools` and call `./tools/aicontext`.
- Without .NET in CI (for example a JS/Python project) you can download the binary instead of `setup-dotnet` and `dotnet tool install`:

```yaml
      - run: |
          curl -fsSL https://github.com/aicontext-dev/aicontext/releases/latest/download/aicontext-linux-x64.tar.gz | tar -xz -C /usr/local/bin aicontext
      - run: aicontext validate --strict
      - run: aicontext export --target all --check
```

### Local check before commit (optional)

A simple git hook `.git/hooks/pre-commit`:

```sh
#!/bin/sh
aicontext validate --strict && aicontext export --target all --check
```

---

## 11. Recipes: typical situations

### A. A new project

```powershell
aicontext init --name my-project
# edit .aicontext/README.md, add constraints.md
aicontext validate
aicontext export --target all
git add .
git commit -m "Add AIContext"
```

### B. I already have a hand-written `AGENTS.md` / `CLAUDE.md` / copilot-instructions

1. Rewrite the content into `.aicontext/` (general description → `README.md`, "never do" → `constraints.md`, architecture explanation → `architecture.md`).
2. `aicontext export --target all` – if a hand-written file must not be overwritten, export ends with a conflict (nothing changes).
3. Once you have moved the content, run `aicontext export --target all --force` – the original file is replaced with the generated one. The original content is in git.

### C. The team started using another tool

Add it to `exports.targets` and run `aicontext export --target all`. You do not have to rewrite anything.

### D. The project is large and the context takes up a lot of space

1. `aicontext status` – look at "Always on".
2. Move specifics into `.aicontext/rules/` with `paths` (chapter 7).
3. Set `"layout": "split"`, `aicontext export --target all`, `aicontext status` – compare.

### E. A monorepo with several services

For each service, a rule file with `paths: ["services/<name>/**"]`, then `"layout": "split"` and optionally `"nestedAgents": true` (chapter 8).

### F. I want to know whether the exports are up to date

```powershell
aicontext status
aicontext export --target all --check
```

### G. Adding a new rule for part of the code

1. Create `.aicontext/rules/api.md` with a header (`paths` or `description`).
2. `aicontext validate`
3. `aicontext export --target all`
4. `aicontext status` – check where the rule ended up.

### H. Turning off / removing AIContext

Delete `.aicontext/`. The generated files (`AGENTS.md`, …) remain as ordinary files – you can keep using them, or delete them.

---

## 12. Troubleshooting

| Message / situation | Cause | Solution |
|---|---|---|
| `Missing required file: …\.aicontext\README.md` | you are not in the project root or did not run `init` | go to the root or use `--path` |
| `context.json /…: …` | configuration error | the path in the message shows the exact place; compare with chapter 4 |
| `unknown property is not allowed` | a typo or non-existent field in `context.json` | fix the field name |
| `Recommended file missing …` | warning | add the file (chapter 3) or ignore; must be added in CI with `--strict` |
| `Possible secret …` | there is a password/key in `.aicontext/` | remove it; for an intentional example add `aicontext:allow-secret` |
| `exists and was not generated by aicontext` | the target has a hand-written file | move its content into `.aicontext/`, then `--force` |
| `Export is out of date` (with `--check`) | the source or a generated file changed | run `aicontext export --target all` and commit |
| `rules/Bad Name.md: file name must use lowercase…` | disallowed rule name | rename it (for example `bad-name.md`) |
| `… is reserved for a built-in section` | the rule name is reserved | choose another name |
| `… frontmatter is not closed with '---'` | the rule header has no closing `---` | add it |
| `path '…' must use '/' as separator` | you used `\` in `paths` | replace it with `/` |
| A rule was not exported separately | `layout` is not `split`; or it is Copilot/Claude on-demand content (it stays in the main file) | check `aicontext status` |
| A rule stayed in the root `AGENTS.md` | the pattern is not an exact existing directory | see `status` (it gives the reason) |
| `exports.nestedAgents has no effect unless exports.layout is 'split'` | `nestedAgents` without `split` | set `"layout": "split"` |
| `… contains both 'windsurf' and 'devin'` | Devin also reads `.windsurf/rules` | keep only one |
| `dotnet tool install` fails | the .NET SDK is missing | install .NET 9 or 10, or use the standalone binary from Releases (Step 1A) |
| `aicontext` is not found after installation | the dotnet tools path is not on `PATH` | open a new terminal; the path is `~/.dotnet/tools` |

---

## 13. How to write good context

- **Short and specific.** AI does not need the whole documentation, but what would otherwise go wrong. Everything in "always on" is loaded all the time.
- **"Why" is more valuable than "what".** From the code, AI sees the structure; from the context it needs reasons and constraints.
- **Phrase rules as prohibitions and obligations** ("never…", "always…") – and put them in `constraints.md`.
- **One rule = one sentence.** Long paragraphs are harder to follow.
- **Remove what is outdated.** Review `.aicontext/` once in a while (and update `metadata.lastReviewed`).
- **No secrets.** Never put passwords, tokens or personal data here.
- **Watch the size with `aicontext status`.** As a rough target: the main ("always on") file in the low single-digit KB; Claude Code, for example, recommends keeping CLAUDE.md under ~200 lines.
- **Put specifics into `rules/`.** What applies only to the UI belongs in a UI rule, not in the README.

---

## 14. Glossary

| Term | Meaning |
|---|---|
| **Target** | the tool a file is generated for (`agents`, `copilot`, …) |
| **Export** | a generated file for a tool; never edited by hand |
| **Layout** | `single` = everything in one file; `split` = small main file + separate rules |
| **Rule** | markdown in `.aicontext/rules/` with optional `paths`/`description` |
| **Scoped** | a rule that applies only to matching files (`paths`) |
| **On-demand** | a rule the assistant loads when it considers it relevant (`description`) |
| **Always on** | content that is always loaded |
| **Core / Extended profile** | the completeness level of `.aicontext/` (chapter 3) |
| **Source-hash** | a fingerprint of the sources in the export header – it changes only when the content changes |
| **Conflict** | an export would overwrite a file that was not generated by aicontext |
| **Frontmatter** | a `---` … `---` block at the start of a markdown file with settings |
| **Glob** | a path pattern, for example `src/**/*.ts` (`*` = any text, `**` = including subdirectories) |

---

More information: the specification [SPEC.md](../SPEC.md), changes in [CHANGELOG.md](../CHANGELOG.md), the project <https://github.com/aicontext-dev/aicontext>.
