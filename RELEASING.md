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

1. Prepare the release on a branch named `release/X.Y.Z`. Do not name the branch `vX.Y.Z`: the tag has that name, a branch and a tag with the same name make `git push origin vX.Y.Z` and `git checkout vX.Y.Z` ambiguous, and the workflow picker then lists two refs with one name.
2. On that branch: set `<Version>` to the same value in `src/AIContext/AIContext.csproj` and `src/AIContext.CLI/AIContext.CLI.csproj` (change `AIContext.Spec` only when you mean to publish a new version of it), close `[Unreleased]` in [CHANGELOG.md](./CHANGELOG.md) with the version and the date, add a short upgrade summary there, and update the row of the version in [SPEC.md](./SPEC.md) section 9.1 and the version in the README and in the first lines of the user manual. Merge through a pull request. A pull request that changes `src/**/*.csproj` also runs the workflow as a dry run; wait for it to be green.
3. Create the tag `vX.Y.Z` on the merge commit and a **published** GitHub release for it (not a draft, and without assets). The workflow attaches the binaries to that release; it does not create it.
4. Rehearsal (recommended, and the first thing to do for the first release made with this workflow): Actions, Publish NuGet Packages, Run workflow, **Use workflow from: Tag vX.Y.Z**, leave **Publish** unticked. Started from the tag it also runs the tag checks and the check of the release; in a dry run a problem that would stop a release is a warning ("Dry run. A release would stop here: ..."), so read the warnings of the first job. Expected: none.
5. Actions, Publish NuGet Packages, Run workflow, **Use workflow from: Tag vX.Y.Z**, tick **Publish**.
6. Check the release assets and the packages on nuget.org.

Publishing the GitHub release does not start the workflow, and a workflow started from a branch cannot publish.

## What the workflow does, in order

1. **resolve**: the commit is `github.sha` of the run, and every later job checks out exactly that SHA, never a branch or tag name. It checks that the ref is a tag named `v<version>`, that the tag points at this commit on the remote, that `AIContext` and `AIContext.CLI` have the tag's version, that no `AIContext`/`AIContext.CLI` version is on nuget.org yet and that the release exists and has no asset of the names it uploads. Everything already known to stop a release stops it here, before any build.
2. **verify**: the checks of a pull request, defined once in [pr-checks.yml](./.github/workflows/pr-checks.yml): strict validation, export check, build, and the tests on .NET 9 and .NET 10 on Linux and Windows.
3. **package**: packs the three packages once (the commit is recorded in each as the repository commit), checks their metadata and contents, and smoke-tests those files: the tool on .NET 10 and .NET 9, and the library on .NET 9 and .NET 10. Only these files are used, never a published copy of the same version.
4. **binaries** ([release-binaries.yml](./.github/workflows/release-binaries.yml)): builds and smoke-tests the standalone binary of each platform; it must report the version of the tag.
5. **upload-assets**, after every job above succeeded: checks the tag again, then attaches the binaries, `SHA256SUMS.txt` and `SOURCE.txt` to the release.
6. **publish-nuget**, after that: checks the tag again, verifies the checksums of the packages, requests the NuGet token as the second to last step and pushes the packages of the plan as the last one.

The reversible part (release assets can be deleted) comes first; the irreversible one (a NuGet version cannot be replaced) comes last.

Without **Publish**, and on pull requests that change release files, the workflow is a dry run: jobs 1 to 4 run, a problem that would stop a release is reported as a warning, nothing is published, and no token is requested. A dry run shows that the packages and the binaries build and pass their tests; it does not show that publishing works (see the next section).

## What has been run, and what has not

State on 2026-11-12. Update this section after the first release made with this workflow.

- **Dry runs (no Publish) on pull requests passed**: pull request #20 (2026-11-11) and pull request #25 (2026-11-12). In both, `resolve`, the four checks of `verify`, the pack and smoke test of the packages, and the standalone binaries on all five platforms succeeded, and `upload-assets` and `publish-nuget` were skipped, as designed. A pull request is not started from a tag, so the tag checks and the check of the release were not exercised.
- **Not run yet**: a run started from a tag, and any run with **Publish** ticked. So the attachment of the binaries to a release (`upload-assets`, `release-assets.sh`), the Trusted Publishing login in its current job (`NuGet/login` in `publish-nuget`) and the push (`nuget-push.sh`) have not been run on GitHub. Versions 0.2.0 to 0.6.0 were published by an earlier shape of this workflow (a manual checkout, pack and push); the same Trusted Publishing policy worked for it, which is a reason to expect the login to work, not a proof for the current jobs.
- The scripts in `.github/scripts/` were run locally on Linux and Windows against local stand-ins for nuget.org and GitHub.

The first real release with this workflow therefore is also the first real run of its irreversible part. That is why the rehearsal from the tag (step 4 above) comes first, and why a failure in `upload-assets` is cheap (assets can be deleted) while one in `publish-nuget` is not (a NuGet version cannot be replaced).

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
- What has and has not been run on GitHub is in the previous section.
