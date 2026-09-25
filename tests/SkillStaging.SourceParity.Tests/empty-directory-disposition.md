# Empty source directory disposition

**Verdict:** empty directories are outside the Game skill package's content-addressed
file set. The F# read-only observer may ignore them after descriptor-pinned, stable
enumeration. It must still refuse a link, special entry, path alias, or undeclared
file encountered during that scan.

Evidence for this contract boundary:

- ADR-0014 makes the producer manifest the delivery contract and says the fan-out
  reads manifests rather than ad-hoc directory scans. The Game `template/README.md`
  describes its schema-v2 `files` list as a closed **per-file digest set**.
- `scripts/generate-skill-manifest.fsx` builds that list with recursive
  `Directory.GetFiles`; it records no directory entries or directory digests.
- The #644 `source_files` selector skips directories, and `stage()` writes the
  manifest plus each declared file path. An empty directory has no staged byte or
  destination to compare.

The prior #651 corpus showed two Python-accept/F#-refuse cases: an empty nested
directory and an extra empty product root. Red-before F# controls reproduced both.
This repair accepts them with unchanged manifest and skill bytes, including
empty-only subtrees. Independent controls keep nonempty undeclared roots, symlinks,
and case-alias directories refused.

The F# observer still refuses a **nonempty** undeclared product root while #644's
selector ignores it. That stricter policy remains a separate acceptance judgement;
these tests establish parity only for the enumerated fixtures and exact output bytes.
#644 acceptance, installed-package proof, and #650's adversarial ABA/post-check
limits remain prerequisites.
