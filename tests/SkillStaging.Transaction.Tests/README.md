# Disposable Game skill stage transaction

This harness compares the exact #644 Python stage candidate with the F#
read-only source plan from #645/#650/#651/#652. It stages only under Python
`TemporaryDirectory` paths. It does not invoke the production receiver, pack
the package, or change the live gate.

The candidate is pinned to SHA-256
`ba060dff1a12cdff4c51c13e0dc478de0721d7b4193c1e19a4ec75852819ba44`
at #644 head `e3105bd9d3076f8a6b09b0c3b8835679e2351e70`.

Run from this branch:

```sh
dotnet build tests/SkillStaging.SourceParity.Observer/SkillStaging.SourceParity.Observer.fsproj -c Release -warnaserror
python3 tests/SkillStaging.Transaction.Tests/stage-transaction.py \
  --candidate /home/developer/projects/FS.GG.Game-fsc06-canonical-review/src/FS.GG.Game.Skills/stage-skills.py
```

The real 17 skill catalog must stage the manifest and 17 skill files with the
same paths and raw bytes as the F# plan. A separate fixture verifies BOM,
CRLF, a nested file, and removal of a previous receiver's obsolete file. It
sets source file modes to `0600` and `0640`; with process umask `022`, the
staged regular files must be `0644`, directories `0755`, and every path owned
by the running uid/gid. The harness now compares these observed facts with the
pure F# receiver contract derived from the source plan and the explicit
uid/gid/umask inputs. These facts describe this disposable Python transaction;
the F# code still performs no output writes.

With an existing receiver, changed, removed, or symlinked source files,
changed manifest bytes, and an extra file inside a selected root must refuse
without changing its paths, bytes, modes, owner, or inode. An injected payload
rename failure checks restoration after the old receiver has moved aside.
Every refusal must leave no temporary stage directory.

#652 currently refuses a nonempty *unselected* product root while #644 ignores
it. Whether to keep that stricter F# policy is an owner acceptance decision;
this harness does not weaken selected-root closed-file checks. #644 owner
acceptance and installed-package parity remain prerequisites. Source and
receiver checks are observations, not an atomic snapshot guarantee against
adversarial ABA, in-place mutation after the final check, concurrent writers,
or a crash between filesystem renames.
