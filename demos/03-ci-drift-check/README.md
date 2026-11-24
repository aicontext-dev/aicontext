# Catch unfinished context and stale exports in CI

Two different mistakes can reach a pull request: unfinished source instructions,
or a source change whose generated files were not updated. This demo shows how
to detect each one, first locally and then with a prepared GitHub Actions
workflow.

Prefer watching? [Watch the animated walkthrough on Vimeo](https://vimeo.com/1249777305)
(2:10). This silent, AI-generated animation follows the written guide, not a
recording of live commands or a hosted CI run. Use the steps below to run the
demo yourself.

**You can finish the local exercise without a GitHub account or Git.**
The optional hosted workflow needs your own GitHub repository. Do not publish
anything just to follow the local steps.

## 1. Prepare a fresh practice folder

Use the **0.7.0 standalone CLI** from [demo 01](../01-basics/README.md).

1. Outside this repository, create `aicontext-ci-practice`. Choose another name
   if it already exists.
2. Copy this demo's **whole `project` folder** into it. Include both
   `.aicontext` and `.github`; on Linux use **Ctrl+H** to show hidden folders.
3. Copy the `tools` folder from your earlier practice folder alongside `project`,
   or [download the CLI](../01-basics/README.md#2-download-the-cli).
4. [Open a terminal in your copied `project`](../01-basics/README.md#3-open-a-terminal-in-your-copy-of-project).
   Keep it there and run only your operating system's commands.

```text
aicontext-ci-practice/
  project/
    .aicontext/
      README.md
      context.json
      architecture.md
      decisions.md
      constraints.md
    .github/
      workflows/
        check-context.yml
  tools/
    aicontext/
      aicontext.exe             Windows; named aicontext on Linux
```

The sample is already initialized. Do not run `init` or use `--force`.
Only `agents` is configured, so export will create one `AGENTS.md`.

## 2. Establish a passing baseline

Unlike the smaller demos, this sample includes the three recommended section
files with real content. It should pass **strict** validation without warnings.

**Windows**

```powershell
..\tools\aicontext\aicontext.exe validate --strict
```

**Linux**

```bash
../tools/aicontext/aicontext validate --strict
```

You should see `AIContext validation passed (Extended profile).`
Extended describes the files present, not the quality of their advice.

Generate the instruction file:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (1 changed, 0 unchanged).`
Open the new `AGENTS.md` to inspect the generated content.

Check it:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

You should see `Export is up to date.`
Both checks now pass.

## 3. Try an unfinished source instruction

Open your copy of `.aicontext/README.md`. Add this line at the end and save:

```text
TODO: Document the review checklist.
```

Run ordinary validation first:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe validate
```

**Linux**

```bash
../tools/aicontext/aicontext validate
```

It reports a placeholder warning, but still passes. Now use strict mode:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe validate --strict
```

**Linux**

```bash
../tools/aicontext/aicontext validate --strict
```

This time validation fails. **That failure is intentional:** `--strict` makes
warnings fail the command too. Neither validation command edits your files.

Remove the added line, including the extra blank line if you added one, and
save. Do not export the unfinished instruction.
Run `validate --strict` again using the command above; it should pass.

## 4. Try a valid change without regenerating the output

Open `.aicontext/constraints.md`. Change:

```text
- Document the expected output for each command example.
```

to:

```text
- Document the expected output and exit code for each command example.
```

Save, then run `validate --strict` again. It should pass: the source is
structurally valid and contains no obvious placeholder.

Now check the exports:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

You should see:

```text
Export is out of date (1 file(s)). Run 'aicontext export' to update.
```

`AGENTS.md` still contains the original rule. This is the second kind of
failure: strict validation does not replace a freshness check.

Successful checks return exit code `0`; both intentional failures above return
`1`. If you want to inspect those numbers, use the
[exit-code commands from demo 01](../01-basics/README.md#optional-see-the-exit-code)
immediately after the command you are checking.

## 5. Repair locally, then check again

Regenerate the instruction file:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (1 changed, 0 unchanged).`
Open or reload `AGENTS.md`; its rule should now include **"and exit code"**.

Run `validate --strict` and `export --target all --check` again using the
commands above. Both should pass.

| Source | Export | Strict validation | Export check |
|---|---|---|---|
| Complete | Up to date | Passes | Passes |
| Contains the example unfinished line | Not regenerated | Fails | Not needed: CI stops at validation |
| Complete, with a changed rule | Old rule | Passes | Fails |
| Complete, with a changed rule | Regenerated | Passes | Passes |

## 6. Inspect the prepared workflow

Open [.github/workflows/check-context.yml](https://github.com/aicontext-dev/aicontext/blob/main/demos/03-ci-drift-check/project/.github/workflows/check-context.yml)
in your copied project. Its steps do the following:

| Step | Purpose |
|---|---|
| Check out the project | Read the files in the pull request |
| Install pinned AIContext CLI | Download the Linux x64 0.7.0 archive, verify its pinned SHA-256, and extract it into the runner's temporary directory |
| Validate source instructions | Run `validate --strict` |
| Check generated instructions | Run `export --target all --check`, without updating anything |

The small shell block is CI setup, not something to paste into your interactive
PowerShell window. GitHub runs it in Bash on an Ubuntu runner. It uses no .NET
SDK, global tool installation, write permission to the repository, or secrets.
If you deliberately upgrade the CLI later, update the version and matching
archive checksum together.

**CI must check the files from the pull request, not silently regenerate them
before checking.** Otherwise it would hide the missing export.

### Optional: use it in your own GitHub repository

This sample workflow is nested under `demos` in the AIContext repository, so
GitHub does not run it here.

To enable it in your own practice repository:

1. Use the **contents of your copied `project`** as that repository's root.
   The workflow must be at the root-level `.github/workflows/check-context.yml`,
   not under a `project` or `demos` directory.
2. Generate `AGENTS.md` locally first. Include `.aicontext`, the workflow, and
   the generated `AGENTS.md` together in the repository. Do not upload `tools`.
   The source-only sample intentionally has no pre-generated output.
3. Open a pull request in that repository. The checks run on the pull request.
   There is also a manual trigger once the workflow is on the default branch.

There is no need to change a real project or upload anything to complete this
demo. The workflow never commits, pushes, or repairs files for you.

## What this demonstrates

- `validate --strict` catches warnings that ordinary validation allows.
- `export --check` catches missing or stale generated files.
- The author repairs files locally; CI only verifies the submitted result.

These are structural and file-consistency checks, not a guarantee of useful
instructions, comprehensive secret detection, or agent behavior.
For more options, see [using AIContext in CI](../../docs/user-manual.md#10-using-it-in-ci).

Return to the [demo catalog](../README.md), or compare the
[scoped-rules demo](../02-scoped-rules/README.md).

Verified with CLI 0.7.0 on Windows x64 (PowerShell 5.1) and Ubuntu 24.04 x64
(Bash, WSL2), rechecked on 2026-11-15. The workflow YAML and its exact install/check
commands were verified locally on Ubuntu/WSL; a hosted GitHub Actions run
was not performed.
