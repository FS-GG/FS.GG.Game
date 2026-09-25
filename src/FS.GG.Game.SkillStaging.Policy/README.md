# Game skill staging policy preparation

This pure F# reducer prepares the `scope: product` subset of the authored skill
manifest. It parses the exact schema-v2 manifest bytes, binds the caller's row
facts and every declared file digest, then validates each product ID, source
root, complete supplied file set, file type fact, and canonical digest. The
returned plan snapshots manifest and source bytes and exposes defensive copies
for a future staging adapter. It performs no filesystem or package writes. The
manifest remains the only delivered-set authority.

The provisional F# policy accepts printable ASCII file paths only. The live
Python stager uses Unicode `casefold()` to detect path aliases, while .NET's
invariant lowercase comparison does not fold every alias (for example,
`Straße.txt` and `STRASSE.txt`). Refusing non-ASCII paths prevents a false
closed-set verdict until an exact cross-runtime casefold policy is qualified.
It is a fail-closed restriction, not installed parity for Unicode paths.

The current Python package stager and package verification gate remain live. The
source-only repair in [Game PR #644](https://github.com/FS-GG/FS.GG.Game/pull/644)
must land and be requalified before a replacement can claim parity. A later
adapter must enumerate every non-directory source entry under each product root,
including symlink and nonregular entries, without following links. It must
provide those complete facts to this reducer, stage atomically, compare packed
output, and qualify the receiver before any production flip. This pure project
cannot establish enumeration completeness or output rollback on its own.

Run the bounded checks with:

```sh
dotnet restore tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj --locked-mode
dotnet build tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj -c Release --no-restore -warnaserror
dotnet run --project tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj -c Release --no-build --no-restore
```
