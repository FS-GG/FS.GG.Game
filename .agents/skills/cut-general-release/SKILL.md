---
name: cut-general-release
description: Audit every active FS-GG GitHub source repository against public NuGet, group stale packages by their repository-owned release workflow, prepare any required version bumps, release all stale units in dependency order, and verify the published artifacts and downstream state. Use when asked to cut, run, or plan a general FS-GG NuGet release or publish everything whose NuGet artifact is behind its GitHub repository.
---

# Cut a general FS-GG release

Release from repository-owned workflows, never by pushing a locally built package. Treat NuGet as the
public read path and GitHub's default branch as the source state to publish.

Use [[cross-repo-coordination]] for claims, cross-repository sequencing, consumer updates, and board
state. A general release is one coordinated operation, but each repository remains the authority for
its package sets, versions, tags, and publishing workflow.

## Safety boundary

- Start with a read-only audit and print the complete release matrix before changing anything.
- An explicit request to **cut/run** the general release authorizes normal release preparation,
  release PRs, tags or workflow dispatches, and verification. A request to **audit/plan/check** does
  not.
- Never publish from an unmerged commit, bypass branch protection, overwrite/move an existing tag,
  republish an immutable NuGet version, or substitute a local `dotnet nuget push`.
- Keep independent units moving when one unit is blocked. Report the blocker; never weaken that
  repository's gate.

## 1. Discover live release units

Query GitHub at execution time; do not use a remembered repository or package list.

1. List active, non-archived source repositories in `FS-GG` and resolve each default branch.
2. Inspect that branch's `.github/workflows/` files and repository release documentation. Retain
   workflows that actually publish one or more packages to `api.nuget.org`.
3. Read each workflow completely. Record:
   - workflow filename and canonical trigger;
   - package IDs and coherent sets;
   - evaluated version source, including independently versioned packages;
   - tag grammar or `workflow_dispatch` inputs;
   - verification jobs, stable/prerelease restrictions, and required feeds.
4. Derive package IDs and versions with the same MSBuild evaluation or workflow command the publisher
   uses. Do not infer them from filenames, assembly names, XML regexes, or the repository name.
5. Model each atomic workflow/coherent set as one **release unit**. Never split a set that its workflow
   publishes together.

Include content-only packages, tools, templates, driver/kit packages, and skills packages when their
owner repository publishes them to NuGet. Exclude samples, tests, archived repositories, GitHub-only
artifacts, and projects with no repository-owned public-NuGet release path.

## 2. Prove freshness

For every package in every release unit, query the public NuGet V3 API and download the candidate
`.nupkg` when it exists. Compare case-insensitive package IDs and normalized SemVer values.

Classify the unit:

- **current** — the repository's intended version exists on NuGet and its package repository commit
  represents the current default-branch commit.
- **releaseable-stale** — the intended version is absent from NuGet.
- **needs-version** — the intended version already exists, but its repository commit is older than
  the current default branch. NuGet packages are immutable; prepare a new version before publishing.
- **blocked** — NuGet is ahead, package ownership/repository metadata disagrees, the release workflow
  cannot identify the same package set/version, a required feed is incoherent, or freshness cannot be
  established safely.

Prefer the nupkg's repository URL and commit metadata. If an older package lacks a repository commit,
resolve its owner workflow's release tag or GitHub Release to a commit and state that fallback in the
matrix. Absence of metadata is not proof that a package is current.

Print one row per release unit:

```text
repository | workflow | packages | GitHub head | NuGet versions | target | state | reason/action
```

Do not start writes until every discovered unit has a row and dependencies between stale units are
known.

## 3. Prepare versions

For each `needs-version` unit:

1. Inspect changes since the published package commit, release notes, and the repository's versioning
   policy.
2. Reuse an already-declared unpublished version when valid.
3. Otherwise choose the smallest SemVer increment justified by the changes. A patch increment is only
   the default for compatible fixes or release-only repository drift; do not silently classify a
   breaking change.
4. Update every version source and coherence record required by that repository, regenerate derived
   artifacts, and run its release dry-run/pack verification.
5. Open a narrowly scoped release PR, let required checks pass, and merge through the protected branch.
   Re-read the merged default-branch SHA and re-audit the target version before release.

If the SemVer impact is ambiguous, block only that unit and request the missing decision while
continuing safe independent units.

## 4. Release in dependency order

Build a producer graph from project/package references, registry pins, and release documentation.
Release leaves before consumers:

1. shared contracts/build substrate;
2. libraries and tools that consume them;
3. templates, skills, manifests, and registries that pin or deliver those packages.

For each unit:

1. Confirm required checks are green at the exact default-branch SHA.
2. Confirm its target version is still absent from NuGet.
3. Invoke the canonical repository workflow exactly once, using its documented tag or dispatch
   contract. Prefer the repository's orchestrator workflow when it owns a multi-tag or rollback
   protocol.
4. Watch every job to a terminal state. On failure, preserve logs and stop dependent units.
5. Never retry by changing the version or using a second trigger until the first run and both feeds
   have been reconciled; a failed run may have partially published.

## 5. Verify and close

A green workflow is necessary but insufficient. For every released package:

- poll public NuGet until the exact version is indexed;
- download it anonymously from NuGet and verify identity/version plus repository URL and commit;
- verify byte identity on every feed required by the owner workflow;
- run the repository's package/install/restore smoke test against the public feed;
- confirm tags and GitHub Releases point at the intended merged commit;
- update registry leads, generated manifests, consumer pins, and lock files required by the release
  contract, then verify those changes through their normal PR gates.

Re-run the full audit. Finish only when every original stale unit is `current`, or is explicitly
reported as blocked with its owner, evidence, and next action. Summarize released package versions,
workflow runs, commits/tags, downstream updates, and untouched current units.
