# Security Policy

## Reporting a vulnerability

Please report a suspected vulnerability **privately** with GitHub private vulnerability reporting: open the **Security** tab of this repository and choose **Report a vulnerability**, or go to <https://github.com/aicontext-dev/aicontext/security/advisories/new>.

Do not open a public issue, discussion or pull request for it, and do not post details (steps, file contents, output with paths) anywhere public until a fix has been released. Never send real secrets; use synthetic ones.

A useful report has:

- the tool version (`aicontext --version`), the operating system and the file system;
- the commands, or a minimal repository, that reproduce it (for links, how they were created);
- what you expected, what happened, and what it lets an attacker do.

AIContext is pre-1.0 and has a single maintainer, so there is no guaranteed response time. Fixes are made on `main` and released in the next version; once released, a fix is described in [CHANGELOG.md](./CHANGELOG.md). Say so in the report if you want to be credited.

## What counts as a vulnerability

A way to make `aicontext`, run on a repository or arguments an attacker controls:

- write, overwrite or delete a file outside the project, or inside a separate project, for example through a symbolic link, junction or crafted path (`export`, `init`);
- read a file outside the project into the generated files, or print the value of a secret;
- overwrite a hand-written file without `--force`, or get past the project boundary with `--force`;
- run code;
- or a weakness in the release workflows or the published packages and binaries.

## Known limits

Some limits are documented and are not vulnerabilities in themselves: the tool is not a sandbox, only symbolic links and junctions are checked (not hard links), an export is not a transaction, the generated-file header is a convention and not authentication, the secret lint is a heuristic, and the content of `.aicontext/` is not judged. They are described in [Security model and limits](./docs/user-manual.md#security-model-and-limits). A way around something that section says the tool does is in scope.

Link handling is tested on Linux and Windows; macOS and network file systems are not.
