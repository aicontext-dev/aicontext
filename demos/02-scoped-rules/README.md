# Keep specialized rules out of the main instructions

Documentation rules are useful when editing documentation, but need not be in
every request. In this demo, start with all content in one file per tool, then
split it into general guidance, a path-scoped rule, and an on-demand checklist.

You will preview the change without writing anything and inspect where each
tool's files put the content. No AI account or application runtime is needed.

## 1. Prepare a fresh practice folder

If needed, [download the repository and CLI as in demo 01](../01-basics/README.md#1-copy-the-sample-project).
Use the **0.7.0 standalone CLI**, not a build of this repository.

1. Outside the repository, create `aicontext-scoped-practice`. Choose a different
   name if it already exists.
2. Copy this demo's **whole `project` folder** into it, including `.aicontext`.
3. Copy the `tools` folder from your demo 01 practice folder alongside `project`,
   or [download the CLI](../01-basics/README.md#2-download-the-cli).
4. [Open a terminal in your copied `project`](../01-basics/README.md#3-open-a-terminal-in-your-copy-of-project).
   On Linux, show hidden folders with **Ctrl+H** and make the executable runnable
   as described there.

```text
aicontext-scoped-practice/
  project/
    .aicontext/
      README.md
      context.json
      constraints.md
      rules/
        docs.md
        review.md
    docs/
      guide.md
    reference/
      overview.md
  tools/
    aicontext/
      aicontext.exe             Windows; named aicontext on Linux
```

The sample is already initialized. Do not run `init` or use `--force`.
Keep your terminal in `project` and use only your operating system's commands.

## 2. Read the different kinds of guidance

Open these files in your copy of `.aicontext`:

| File | Purpose |
|---|---|
| [constraints.md](./project/.aicontext/constraints.md) | General guidance, kept in each tool's main file |
| [rules/docs.md](./project/.aicontext/rules/docs.md) | A rule scoped to files below `docs` |
| [rules/review.md](./project/.aicontext/rules/review.md) | A checklist whose relevance a tool may decide on demand |

The documentation rule starts with:

```yaml
---
description: Guidance for documentation pages
paths: ["docs/**"]
---
```

This header is called **frontmatter**. `docs/**` means files anywhere below the
project's `docs` directory. It includes `docs/guide.md`, but not
`reference/overview.md`. Use `/` in these patterns, even on Windows, and keep
the key `paths` lowercase.

The review checklist has a `description` but no `paths`. It is **on-demand**,
not scoped. A description is not a path restriction.

The [configuration](./project/.aicontext/context.json) selects only `copilot`
and `cursor`, starting with `"layout": "single"`. These targets illustrate
different ways of representing on-demand content.

## 3. Export the single-file layout

First validate the sample.

**Windows**

```powershell
..\tools\aicontext\aicontext.exe validate
```

**Linux**

```bash
../tools/aicontext/aicontext validate
```

Validation should pass with the Core profile. Warnings about missing
`architecture.md` and `decisions.md` are expected in this small example.

Export both configured targets:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (2 changed, 0 unchanged).`
Open `.github/copilot-instructions.md` and `.cursor/rules/project.mdc`.
Both contain the documentation rule and review checklist along with the general
guidance. The scope is written as text, not a separate native rule file.

Inspect the current layout:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe status
```

**Linux**

```bash
../tools/aicontext/aicontext status
```

Look for `Layout: single` and both targets marked `in sync`. Spacing and size
estimates can vary; you do not need to compare exact byte or token counts.

## 4. Change the layout and preview the export

In your copy of `.aicontext/context.json`, change only:

```json
"layout": "single"
```

to:

```json
"layout": "split"
```

Save with **Ctrl+S**. Ask what would happen before writing the new files:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --dry-run
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --dry-run
```

The preview reports two files it **would update** and three it **would create**.
The existing files still contain the original single-file layout. Check in
your editor: the new rule files have not appeared. `--dry-run` is a preview,
not a freshness test that must fail when changes are pending.

## 5. Write and inspect the split files

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (5 changed, 0 unchanged).`
The generated files now look like this:

```text
project/
  .github/
    copilot-instructions.md
    instructions/
      aicontext-docs.instructions.md
  .cursor/
    rules/
      project.mdc
      aicontext-docs.mdc
      aicontext-review.mdc
```

Open the files and compare:

| Content | Copilot output | Cursor output |
|---|---|---|
| General guidance | Main instructions | `project.mdc` |
| Documentation rule | Separate file with `applyTo: "docs/**"` | Separate file with `globs: docs/**` |
| Review checklist | Stays in the main instructions with a `Use when:` line | Separate on-demand file with a `description` |

The documentation rule should no longer be in either main file.
In the scoped Copilot file, its description is kept as a `Description:` line
in the body, **not** as a second frontmatter trigger.
The Cursor review file has no path scope; do not mistake it for a scoped rule.

This is why `split` does not mean every tool loads the same amount of content.
AIContext preserves the guidance, using the forms each target supports.
There is no promised percentage reduction, especially for such a tiny sample.

## 6. Check the result

**Windows**

```powershell
..\tools\aicontext\aicontext.exe status
```

**Linux**

```bash
../tools/aicontext/aicontext status
```

Look for `Layout: split`, the `docs` rule listed as scoped, the `review` rule
listed as on-demand, and both targets `in sync`. The table separates always-on
content from scoped/on-demand content.

Finally, check the exported files:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

You should see `Export is up to date.`

## What this demonstrates

- `paths` makes a rule scoped; `description` alone makes it on-demand.
- `single` includes the rules in the main files; `split` uses native separate
  rule files where supported.
- `--dry-run` previews the planned changes without writing them.
- `status` explains the layout and estimates the context sizes.

This verifies generated files, not a live Copilot or Cursor session.
Actual loading depends on the tool, version, settings, and harness.
Some targets cannot split content at all, and an unsupported scope translation
is refused rather than silently broadened. Read the
[rules and split-layout reference](../../docs/user-manual.md#7-rules-and-the-split-layout-for-larger-projects)
before using more complex patterns.

Next: [catch unfinished context and stale exports in CI](../03-ci-drift-check/README.md),
or return to the [demo catalog](../README.md).

Verified with CLI 0.7.0 on Windows x64 (PowerShell 5.1) and Ubuntu 24.04 x64
(Bash, WSL2), rechecked on 2026-11-15. The preview preserved existing file contents and
modification times, and the final exports were identical across platforms.
