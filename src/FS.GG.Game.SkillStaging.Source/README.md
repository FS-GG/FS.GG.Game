# Read-only physical source candidate

`ReadOnlySource.capture` traverses `template/product-skills` from a repository root. It checks
each path with Linux `statx(AT_SYMLINK_NOFOLLOW)`, rejects links and nonregular entries,
rejects empty or undeclared directories and case aliases, reads each regular file's exact
bytes, then passes all observed product files to the pure `Policy.prepare` reducer.

This candidate has no destination path or write operation. Its returned `StagePlan` owns
copies of the manifest and source bytes. It is stricter than the current Python stager
on empty and undeclared product directories; acceptance parity remains pending on #644.

The `statx`, directory enumeration, and `ReadAllBytes` calls are path based. A concurrent
replacement can change what is read between calls. This candidate is Linux only and does
not establish atomic no-follow capture, staging rollback, package publication readiness,
or installed package parity. The owner must validate those contracts before wiring it to
an output-writing stage.
