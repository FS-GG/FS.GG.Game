# Game skill staging policy preparation

This pure F# reducer prepares the `scope: product` subset of the authored skill
manifest. It parses the exact schema-v2 manifest bytes, binds the caller's row
facts and each product row's single `SKILL.md` file digest, then validates each
product ID, source path, source file fact, and canonical digest. The returned
plan snapshots manifest and skill bytes and exposes defensive copies for a
future staging adapter. It performs no filesystem or package writes. The
manifest remains the only delivered-set authority.

The current Python package stager and package verification gate remain live. The
source-only repair in [Game PR #644](https://github.com/FS-GG/FS.GG.Game/pull/644)
must land and be requalified before a replacement can claim digest parity. A later
adapter must parse the complete manifest, obtain trustworthy regular-file and
symlink facts without following links, stage atomically, compare packed output,
and qualify the receiver before any production flip. This project supplies none
of those activation claims.

Run the bounded checks with:

```sh
dotnet restore tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj --locked-mode
dotnet build tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj -c Release --no-restore -warnaserror
dotnet run --project tests/SkillStaging.Policy.Tests/SkillStaging.Policy.Tests.fsproj -c Release --no-build --no-restore
```
