# Releasing AIContext

A release is made by one workflow, [publish.yml](./.github/workflows/publish.yml), started from the release tag. It releases one commit: the NuGet packages and the standalone CLI binaries come from the same run, and every file that is published is a file that was tested.

## What a release contains

| Part | Where | Version |
|---|---|---|
| `AIContext` (library) and `AIContext.CLI` (tool) | nuget.org | the tag without the `v`; both projects must have it |
| `AIContext.Spec` (placeholder package) | nuget.org | its own, independent of the tag; pushed only when its version is not on nuget.org yet, otherwise skipped with a notice |
| `aicontext-<rid>` archives for Windows x64, Linux x64/arm64, macOS x64/arm64 | assets of the GitHub release | the tag |
| `SHA256SUMS.txt` and `SOURCE.txt` (tag, commit, workflow run) | assets of the GitHub release | the tag |

## How to make a release

1. Set `<Version>` to the same value in `src/AIContext/AIContext.csproj` and `src/AIContext.CLI/AIContext.CLI.csproj`. Change `AIContext.Spec` only when you mean to publish a new version of it. Update [CHANGELOG.md](./CHANGELOG.md), and merge through a pull request.
2. Create the tag `vX.Y.Z` on the merge commit and a **published** GitHub release for it. The workflow attaches the binaries to that release; it does not create it.
3. Optional rehearsal: Actions, Publish NuGet Packages, Run workflow, choose the tag and leave **Publish** unticked. It runs everything below except the two publishing jobs.
4. Actions, Publish NuGet Packages, Run workflow, **Use workflow from: Tag vX.Y.Z**, tick **Publish**.
5. Check the release assets and the packages on nuget.org.

Publishing the GitHub release does not start the workflow, and a workflow started from a branch cannot publish.

## What the workflow does, in order

1. **resolve**: the commit is `github.sha` of the run, and every later job checks out exactly that SHA, never a branch or tag name. It checks that the ref is a tag named `v<version>`, that the tag points at this commit on the remote, that `AIContext` and `AIContext.CLI` have the tag's version, that no `AIContext`/`AIContext.CLI` version is on nuget.org yet and that the release exists and has no asset of the names it uploads. Everything already known to stop a release stops it here, before any build.
2. **verify**: the checks of a pull request, defined once in [pr-checks.yml](./.github/workflows/pr-checks.yml): strict validation, export check, build, and the tests on .NET 9 and .NET 10 on Linux and Windows.
3. **package**: packs the three packages once (the commit is recorded in each as the repository commit), checks their metadata and contents, and smoke-tests those files: the tool on .NET 10 and .NET 9, and the library on .NET 9 and .NET 10. Only these files are used, never a published copy of the same version.
4. **binaries** ([release-binaries.yml](./.github/workflows/release-binaries.yml)): builds and smoke-tests the standalone binary of each platform; it must report the version of the tag.
5. **upload-assets**, after every job above succeeded: checks the tag again, then attaches the binaries, `SHA256SUMS.txt` and `SOURCE.txt` to the release.
6. **publish-nuget**, after that: checks the tag again, verifies the checksums of the packages, requests the NuGet token as the second to last step and pushes the packages of the plan as the last one.

The reversible part (release assets can be deleted) comes first; the irreversible one (a NuGet version cannot be replaced) comes last.

Without **Publish**, and on pull requests that change release files, the workflow is a dry run: jobs 1 to 4 run, a problem that would stop a release is reported as a warning, nothing is published, and no token is requested.

## Nothing is replaced

- There is no `--clobber` and no `--skip-duplicate`. An existing release asset of these names, or an existing `AIContext` or `AIContext.CLI` version on nuget.org, stops the run before anything is published.
- If the tag was moved after the run started, the run stops; start again from the tag.
- A NuGet version cannot be replaced, so if the push of the first package succeeded and a later one failed, that version is partly published and **cannot be finished** by running the job again (the plan stops at the existing version). Fix the cause and release the next patch version; you can unlist the partial one on nuget.org.
- If the binaries were attached and the NuGet push failed before any package was pushed, use "Re-run failed jobs": the artifacts of the run are reused, nothing is rebuilt.
- If assets are wrong, delete them on the release page yourself and run the workflow again. The workflow never does it for you.

## One-time setup by the owner

This repository cannot verify these, and the workflow does not change them:

- **nuget.org**: a Trusted Publishing policy for this repository with the workflow file `publish.yml` (the file name matters), and the NuGet user in the `NuGet/login` step. The environment field stays empty unless the job gets `environment:`.
- **GitHub**: branch protection refers to the job names of `pr-checks.yml`; they are unchanged. Recommended and not done here: a ruleset that protects `v*` tags, and a `release` environment with required reviewers for the `publish-nuget` job (then add `environment: release` to it and the same environment to the NuGet policy).

## Limits

- The binaries and the checksums are not signed. `SHA256SUMS.txt` and `SOURCE.txt` document where they come from; they are not proof of it. The NuGet packages are signed by nuget.org after the push, so their hash differs from the one of the file in the run.
- External actions are referenced by major version, not by commit SHA.
- The scripts in `.github/scripts/` were run locally on Linux and Windows against local stand-ins for nuget.org and GitHub. A run of the workflow on GitHub has not been done; the first dry run on a pull request is the first real test of the cloud part.
