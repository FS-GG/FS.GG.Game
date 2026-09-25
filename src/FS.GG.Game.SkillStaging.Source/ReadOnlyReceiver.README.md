# Read-only Game receiver observation

`ReadOnlyReceiver.observe` opens every receiver path relative to a held Linux
directory descriptor with `O_NOFOLLOW`. It uses `statx(AT_EMPTY_PATH)` on each
opened entry for type, mode, uid, and gid, reads regular-file bytes twice from
the opened descriptor, and enumerates directories twice. Directory change
stamps are checked again after visiting children. `verify` passes the resulting
metadata and exact bytes to `ReceiverContract.verifyComplete` from #656. The
observer has no output write operation.

The disposable tests demonstrate a path-based false green: metadata captured
before replacing a file path and byte-identical foreign bytes read through the
replacement symlink satisfy the pure contract. The new observer refuses a
symlink before open and a changed parent roster after an opened-file path swap.
It also refuses late additions, changed bytes, wrong mode or owner, extra
entries, FIFO entries, and a symlinked receiver root.

```sh
dotnet restore tests/SkillStaging.ReceiverObserver.Tests/SkillStaging.ReceiverObserver.Tests.fsproj --locked-mode
dotnet build tests/SkillStaging.ReceiverObserver.Tests/SkillStaging.ReceiverObserver.Tests.fsproj -c Release --no-restore -warnaserror
dotnet run --project tests/SkillStaging.ReceiverObserver.Tests/SkillStaging.ReceiverObserver.Tests.fsproj -c Release --no-build --no-restore
```

This is Linux-only and requires little-endian `statx` and `getdents64`
support. Held descriptors bind individual reads, but the result is not an
atomic receiver snapshot. ABA, post-check mutation, timestamp granularity,
concurrent chmod/chown, ACLs, setgid and umask effects, and crash rollback
remain outside this check. #644/#646 acceptance and installed-package parity
are separate prerequisites before any production receiver use.
