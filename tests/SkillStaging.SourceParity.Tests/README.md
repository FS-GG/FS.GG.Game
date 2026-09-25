# FSC-06 source-only parity corpus

This corpus compares the exact in-memory output paths and bytes projected by the
draft #644 Python source selector (`e3105bd9d3076f8a6b09b0c3b8835679e2351e70`)
with the read-only F# observer stacked on #650. It pins the Python file SHA-256 to
`ba060dff1a12cdff4c51c13e0dc478de0721d7b4193c1e19a4ec75852819ba44`.
The comparison includes the manifest bytes and each `skills/<id>/<file>` byte payload.

Build and run from the Game worktree containing this test:

```sh
dotnet build tests/SkillStaging.SourceParity.Observer/SkillStaging.SourceParity.Observer.fsproj -c Release -warnaserror
python3 tests/SkillStaging.SourceParity.Tests/source-parity.py --candidate /path/to/644/src/FS.GG.Game.Skills/stage-skills.py
```

The runner imports #644's `selected_skills` and `canonical_digest` functions and
projects the bytes they select. It never calls `stage()`. Temporary fixtures contain
source files and manifests; the observer writes only JSON to stdout. The real
17-skill catalog and an independently authored nested BOM/CRLF case require exact
paths and bytes. Malformed and alias fixtures require both implementations to refuse.
Two empty-directory fixtures record known Python-accept/F#-refuse differences.

This is a source-only comparison of candidate logic. #644 acceptance and installed
package proof remain prerequisites. It does not establish output rollback, production
staging parity, or an atomic filesystem snapshot against adversarial ABA mutation.
