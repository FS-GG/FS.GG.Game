# Undeclared Game product root

**Verdict:** #644 at `e3105bd9d3076f8a6b09b0c3b8835679e2351e70` accepts a
nonempty `template/product-skills/<id>/` root absent from the manifest. This is
consistent with its manifest-selected staging contract.
No Python source repair is proposed for this case.

[ADR-0014](https://github.com/FS-GG/.github/blob/main/docs/adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)
makes the producer manifest the delivery contract and directs fan-out to read the
manifest rather than select skills by directory scan. The Game
[manifest generator](../../scripts/generate-skill-manifest.fsx) records a catalog of
product rows and hashes recursive files **within each selected source root**.
The [#644 stager](../../src/FS.GG.Game.Skills/stage-skills.py) validates that every
selected root's physical files equal its declared `files` set, then copies only
those declared files and the manifest. It does not select an extra root merely
because that directory exists.

The [source-only controls](undeclared-root-contract.py) project exact bytes without
calling `stage()`. Adding a nonempty undeclared root leaves the projected manifest
and selected skill bytes unchanged; adding its manifest row selects its exact bytes.
Adding an undeclared file, a symlink, or removing the declared file **inside the
selected root** is refused. A blanket Python refusal
for every unlisted root would add a global source-root policy beyond the closed
per-row file contract; this review does not establish that policy as accepted.

The separate F# observer draft #652 currently refuses a nonempty undeclared root.
That is a stricter candidate policy and remains a parity/acceptance judgement, not
evidence that #644 emits undeclared bytes. #644 owner acceptance, installed-package
proof, and the F# observer's adversarial ABA/post-check limits remain prerequisites.
