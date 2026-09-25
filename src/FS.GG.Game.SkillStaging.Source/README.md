# Read-only physical source candidate

`ReadOnlySource.capture` traverses `template/product-skills` from a repository root.
It opens each path component relative to a held directory descriptor with `O_NOFOLLOW`,
checks the opened descriptor with `statx(AT_EMPTY_PATH)`, and enumerates directories
through `getdents64` on held descriptors. Each directory scan compares supported
mtime/ctime and identity fields before and after, seeks the same descriptor to the
start, repeats the name scan, and refuses a changed stamp or roster. It rejects links,
nonregular entries, undeclared files, nonempty undeclared product roots, and case aliases.
Empty directories carry no manifest file bytes and are ignored after a checked scan. Regular-file
bytes are read twice through a duplicate of the opened descriptor; identity, size,
mtime, and ctime are compared before, between, and after the reads. The accepted bytes
then pass to the pure `Policy.prepare` reducer.

This candidate has no destination path or write operation. Its returned `StagePlan` owns
copies of the manifest and source bytes. It is stricter than the current Python stager
on empty and undeclared product directories; acceptance parity remains pending on #644.

The tests reproduce the former path-probe/read gap: a symlink substituted after a
regular-file `statx` probe made a path read return foreign bytes. The descriptor reader
keeps the opened file and parent directory identities across equivalent swaps, and
refuses a symlink introduced before the child `openat` call.

The controls show that a rename/restore during one `getdents64` pass was accepted
before the stamp check; the new reader refuses that mutation, an added entry, and a
removed entry. This is a best-effort instability check, not an atomic directory
snapshot: a roster can change after the check, and timestamp granularity or an
adversarial filesystem can hide a change. A red-before control showed that an in-place
overwrite after the first read chunk previously returned an accepted plan with the
old bytes. The new reader refuses that overwrite and a same-byte rewrite during
the read. File content can still change after the final check, and an ABA mutation
or an adversarial filesystem can hide a change. Digest checks bind the copied bytes
to the manifest but do not prove a stable filesystem snapshot.
This candidate requires little-endian Linux and libc `getdents64`/`statx` support; it
does not establish staging rollback, package publication readiness, or installed package
parity. The owner must validate those contracts before wiring it to an output-writing
stage.
