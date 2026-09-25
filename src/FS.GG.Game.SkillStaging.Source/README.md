# Read-only physical source candidate

`ReadOnlySource.capture` traverses `template/product-skills` from a repository root.
It opens each path component relative to a held directory descriptor with `O_NOFOLLOW`,
checks the opened descriptor with `statx(AT_EMPTY_PATH)`, and enumerates directories
through `getdents64` on held descriptors. It rejects links, nonregular entries, empty
or undeclared directories, and case aliases. Regular-file bytes are read through a
duplicate of the opened descriptor, then passed to the pure `Policy.prepare` reducer.

This candidate has no destination path or write operation. Its returned `StagePlan` owns
copies of the manifest and source bytes. It is stricter than the current Python stager
on empty and undeclared product directories; acceptance parity remains pending on #644.

The tests reproduce the former path-probe/read gap: a symlink substituted after a
regular-file `statx` probe made a path read return foreign bytes. The descriptor reader
keeps the opened file and parent directory identities across equivalent swaps, and
refuses a symlink introduced before the child `openat` call.

The directory roster is not frozen while `getdents64` runs or after it returns. The
contents of an already opened regular file can also change during a read. Digest checks
bind the copied bytes to the manifest but do not prove a stable filesystem snapshot.
This candidate requires little-endian Linux and libc `getdents64`/`statx` support; it
does not establish staging rollback, package publication readiness, or installed package
parity. The owner must validate those contracts before wiring it to an output-writing
stage.
