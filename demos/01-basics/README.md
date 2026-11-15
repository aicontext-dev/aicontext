# Keep two AI instruction files in sync

Imagine keeping the same project rules in two files for different AI tools.
You change one copy, but forget the other. Now the instructions disagree.

In this demo, you will generate both files from one source, change a rule
yourself, and see how AIContext detects and fixes the mismatch.
Run one short command at a time and inspect the results in your text editor.

## Before you start

You need a file manager, a plain-text editor such as VS Code or Notepad,
and a terminal: **PowerShell on Windows** or **Bash on Linux**.
No Git, .NET SDK, or AI account is needed. There is no application to build.

The steps are shared. Where a command differs, use only the block for your
operating system. The demo uses the **0.7.0 standalone CLI** and does not
change your PATH or install a global tool.

## 1. Copy the sample project

1. Open the [AIContext repository](https://github.com/aicontext-dev/aicontext).
   Select **Code > Download ZIP**, then extract the download.
   If you already have a checkout, you can use it instead.
2. In your file manager, open `demos`, then `01-basics` in the extracted
   repository. You will find the sample folder named `project`.
3. Outside the repository, create a new folder named `aicontext-demo-practice`.
   If that name already exists, choose another; do not overwrite existing work.
4. Copy the **whole `project` folder** into your practice folder.
   Copying the whole folder includes the `.aicontext` directory inside it.
5. In your practice folder, create a `tools` folder, then an `aicontext`
   folder inside `tools`.

Your practice folder should contain:

```text
aicontext-demo-practice/
  project/
    .aicontext/
      README.md
      context.json
      constraints.md
  tools/
    aicontext/                  empty until the next step
```

On Linux, folders whose names start with a dot are hidden in many file managers.
Press **Ctrl+H** to show them. The sample is already initialized, so **do not
run `init`**. There are deliberately no generated instruction files yet.

## 2. Download the CLI

Open the [0.7.0 release page](https://github.com/aicontext-dev/aicontext/releases/tag/v0.7.0).
Under **Assets**, download the archive for your system:

| System | Archive |
|---|---|
| Windows x64 | `aicontext-win-x64.zip` |
| Linux x64 | `aicontext-linux-x64.tar.gz` |
| Linux arm64 | `aicontext-linux-arm64.tar.gz` |

On Linux, if you are unsure which architecture you have, run `uname -m` in
a terminal: `x86_64` means x64, and `aarch64` means arm64.

Before extracting, [verify the download](#verify-the-download).
Extract the archive using **Extract All** on Windows or your Linux archive
manager. Copy the extracted executable and `LICENSE` into `tools/aicontext`.
The executable is named `aicontext.exe` on Windows and `aicontext` on Linux.
Do not leave it inside an extra archive-named subfolder.

The binaries are not code-signed yet. For Windows warnings, see the
[installation notes](../../docs/user-manual.md#step-1-installation).

## 3. Open a terminal in your copy of `project`

**Windows:** In File Explorer, open the practice folder's `project` directory.
Click the **address bar**, type `powershell`, and press **Enter**.

**Linux:** In your file manager, open the practice folder's `project` directory
and use **Open in Terminal** (the wording depends on your desktop).
Give the downloaded program permission to run:

```bash
chmod +x ../tools/aicontext/aicontext
```

Keep the terminal in `project` for the rest of the demo. Copy each command
into it and press **Enter**. Do not copy the output examples or Markdown fences.

Check the version:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe --version
```

**Linux**

```bash
../tools/aicontext/aicontext --version
```

On either system, you should see:

```text
aicontext 0.7.0
Specification version: 0.1.0
```

The prefix before `--version` locates the downloaded program. `..` means
"the parent folder"; the rest is its location inside `tools`.
The words after the program name tell AIContext what to do.

If the program cannot be found, check the extraction location and make sure
your terminal is in `project`, not in the repository or the outer practice
folder. On Linux, filenames are case-sensitive.

## 4. Read the three source files

Open your copied `.aicontext` folder in a plain-text editor.
You do not need to edit anything yet.

| File | What it contains |
|---|---|
| [README.md](./project/.aicontext/README.md) | The short project description that will appear in both outputs |
| [context.json](./project/.aicontext/context.json) | Project name and the two selected output formats: `agents` and `copilot` |
| [constraints.md](./project/.aicontext/constraints.md) | The shared documentation rules you will change later |

The rules are:

```markdown
# Documentation rules

- Use project-relative paths in documentation examples.
- Document the expected output for each command example.
```

In the configuration, `"layout": "single"` means one output file per format.
The example is intentionally small; you do not need to learn the rest of the
configuration to try it.

## 5. Check the sources

**Windows**

```powershell
..\tools\aicontext\aicontext.exe validate
```

**Linux**

```bash
../tools/aicontext/aicontext validate
```

You should see `AIContext validation passed (Core profile).`
Two warnings about missing `architecture.md` and `decisions.md` are expected:
those are recommended files that this small example does not need.
Validation checks structure and obvious placeholders, not whether an AI agent
will follow your rules.

## 6. Generate and inspect both instruction files

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

Here, `all` means **the two targets in this sample's configuration**, not every
supported tool. You should see `Export completed (2 changed, 0 unchanged).`

In your copy of `project`, open the two new files:

- `AGENTS.md`
- `.github/copilot-instructions.md` (on Windows, `.github\copilot-instructions.md`)

Both should contain your project description and the two rules.
Their titles differ because they are written for different tools.
**Read them, but do not edit them:** `.aicontext` is the source you maintain.
On Linux, use **Ctrl+H** if the new `.github` folder is hidden.

Now check whether these outputs match their source:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

You should see `Export is up to date.` The `--check` option only checks;
it does not update the files.

## 7. Change one rule and detect the mismatch

Open your copy of `.aicontext/constraints.md`. Change only the second rule.

Before:

```text
- Document the expected output for each command example.
```

After:

```text
- Document the expected output and exit code for each command example.
```

Save with **Ctrl+S**. You have changed the source, but have not regenerated
the outputs. Run the check again:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

On either system, the output includes:

```text
Export is out of date (2 file(s)). Run 'aicontext export' to update.
```

**This is expected, not a mistake.** Both generated files still contain the old
rule. Open or reload them in your editor to see that `--check` has not changed
their content.

## 8. Bring both files up to date

Run the export without `--check`:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (2 changed, 0 unchanged).`
Open or reload both generated files. Their second rule should now include
**"and exit code"**. You edited one source file, and AIContext updated both
outputs. The generated source-hash header changes too; leave that header alone.

The rule change is the same in both outputs (the changed source-hash headers
are omitted here):

```diff
--- AGENTS.md (before)
+++ AGENTS.md (after)
 - Use project-relative paths in documentation examples.
-- Document the expected output for each command example.
+- Document the expected output and exit code for each command example.
```

```diff
--- .github/copilot-instructions.md (before)
+++ .github/copilot-instructions.md (after)
 - Use project-relative paths in documentation examples.
-- Document the expected output for each command example.
+- Document the expected output and exit code for each command example.
```

Check again:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all --check
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all --check
```

You should see `Export is up to date.`

## 9. Repeat the export: nothing more changes

Without editing any files, run:

**Windows**

```powershell
..\tools\aicontext\aicontext.exe export --target all
```

**Linux**

```bash
../tools/aicontext/aicontext export --target all
```

You should see `Export completed (0 changed, 2 unchanged).`
There was nothing new to write.

**That is the everyday workflow:** edit `.aicontext`, export, then check.
You do not need to keep separate copies of each rule up to date by hand.
To start over, make a fresh copy of the original sample in a new practice
folder; you do not need `--force`.

## What you have learned

- `.aicontext` contains the files you edit.
- `validate` checks those source files.
- `export` creates or updates the instruction files.
- `export --check` reports whether they match the source, without changing them.

This demonstrates file synchronization, not agent behavior. Some tools load
both `AGENTS.md` and Copilot instructions, potentially duplicating context.
These two targets are an example, not a universal configuration recommendation.
The specification is a draft and the CLI is pre-1.0.

### Verify the download

Download `SHA256SUMS.txt` from the same release page. Open a terminal in the
folder containing the downloaded archive and run the command for your system:

**Windows**

```powershell
Get-FileHash .\aicontext-win-x64.zip -Algorithm SHA256
```

**Linux x64**

```bash
sha256sum aicontext-linux-x64.tar.gz
```

For Linux arm64, use `aicontext-linux-arm64.tar.gz` as the filename instead.
Compare the displayed hash with the entry for that exact archive in
`SHA256SUMS.txt` (letter case does not matter). If they differ, do not run the
binary.

### Optional: see the exit code

An exit code is a number that automated checks can read. Immediately after an
AIContext command, run:

**Windows**

```powershell
echo $LASTEXITCODE
```

**Linux**

```bash
echo $?
```

Successful commands in this demo return `0`. The deliberately stale check in
step 7 returns `1`, which can also fail a CI freshness check. You do not need
this extra command to complete the walkthrough.

### Next steps

Continue with [scoped rules and the split layout](../02-scoped-rules/README.md),
or [strict validation and CI checks](../03-ci-drift-check/README.md).

To create sources for your own project rather than copy a sample, see
[`init` in the quick start](../../docs/user-manual.md#step-2-create-the-skeleton).
You can also explore [supported tools and limits](../../README.md#supported-tools-and-their-limits)
or [report what was confusing](https://github.com/aicontext-dev/aicontext/issues/31).
Do not post private code, personal paths, or secrets. Security reports belong
in the [private reporting channel](../../SECURITY.md), not a public issue.

Verified with CLI 0.7.0 on Windows x64 (PowerShell 5.1) and Ubuntu 24.04 x64
(Bash, WSL2), rechecked on 2026-11-15. Technical checks confirmed that the stale check
wrote nothing and the repeated export preserved file contents and modification
times. The Linux arm64 archive is available, but was not tested for this demo.
