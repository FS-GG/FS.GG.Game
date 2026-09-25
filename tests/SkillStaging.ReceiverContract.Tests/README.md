# Pure Game receiver metadata contract

`ReceiverContract.expected` derives the receiver root, intermediate directories,
manifest file, and staged skill files from a `Policy.StagePlan`. Given an
explicit effective uid, gid, and process umask, it models #644's newly created
directory mode as `0777 & ~umask` and file mode as `0666 & ~umask`. The pure
`verify` function refuses duplicate, missing, and unexpected paths, wrong
entry kind, wrong mode, and wrong uid or gid. It reads no filesystem and writes
no output.

The test uses independently authored metadata rows for a nested two-file plan,
then changes each field separately to prove refusal. It also derives a
contract from the real 17-skill tree through the #650 read-only source capture.

```sh
dotnet restore tests/SkillStaging.ReceiverContract.Tests/SkillStaging.ReceiverContract.Tests.fsproj --locked-mode
dotnet build tests/SkillStaging.ReceiverContract.Tests/SkillStaging.ReceiverContract.Tests.fsproj -c Release --no-restore -warnaserror
dotnet run --project tests/SkillStaging.ReceiverContract.Tests/SkillStaging.ReceiverContract.Tests.fsproj -c Release --no-build --no-restore
```

The #654 disposable transaction harness supplies its effective uid/gid and
umask `022` to the F# observer, then compares its independently observed
receiver metadata against the resulting contract. This describes that
disposable receiver, not a production output transaction. The owner and umask
are explicit assumptions; setgid parents, ACLs, concurrent chmod/chown,
adversarial ABA, later mutation, and a crash between renames remain outside
the proof. #644 acceptance, #652's stricter unselected-root policy decision,
and installed-package parity remain separate gates.
