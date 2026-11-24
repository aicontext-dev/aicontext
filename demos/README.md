# AIContext demos

Small, hands-on examples of managing AI coding instructions. Each demo has
its own guide and a sample project you can copy to a separate practice folder.
No AI account or application runtime is needed.

New to the project? [Watch the animated overview on Vimeo](https://vimeo.com/1249768593)
(1:30), or start with the first written demo below.

| Demo | What you will learn | Platforms | Video on Vimeo |
|---|---|---|---|
| [01 - Keep two instruction files in sync](./01-basics/README.md) | Generate two files, change one source rule, detect stale exports, and update them | Windows and Linux | [Watch demo 01 (2:31)](https://vimeo.com/1249775356) |
| [02 - Scoped rules and split layout](./02-scoped-rules/README.md) | Compare layouts, preview changes, and inspect scoped versus on-demand rules | Windows and Linux | [Watch demo 02 (2:18)](https://vimeo.com/1249776219) |
| [03 - Strict validation and CI checks](./03-ci-drift-check/README.md) | Catch unfinished instructions and stale exports locally, then inspect a ready-to-copy GitHub Actions workflow | Windows and Linux; optional GitHub Actions | [Watch demo 03 (2:10)](https://vimeo.com/1249777305) |

The videos are silent, AI-generated animations based on the AIContext 0.7.0
documentation. Terminal sequences are illustrative, not recordings of live
CLI commands or hosted CI runs. Use the written guides to run the demos yourself.

**Start with demo 01.** It uses a pinned standalone CLI, so you do not need to
build this repository or install the .NET SDK.

The sample projects contain sources, not pre-generated instruction files.
Follow the guide on your own copy; do not run exports in this repository.
CLI downloads and local verification recordings do not belong in these
sample directories.

Demo 03 includes a workflow file, but nothing runs remotely just because you
copy the sample. Its local exercise needs no GitHub account; enabling hosted
CI in your own repository is optional.

For reference documentation rather than a guided exercise, see the
[user manual](../docs/user-manual.md).

Tried a demo, or decided AIContext was not for you? Share your experience in
[First-use feedback](https://github.com/aicontext-dev/aicontext/issues/31).
A few sentences are enough; negative feedback is welcome too. Do not include
private code, personal paths or secrets. Suspected vulnerabilities belong in the
[private security reporting process](../SECURITY.md), not the public feedback issue.
