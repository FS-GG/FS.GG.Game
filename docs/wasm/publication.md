# Shared WebAssembly release set

Published `0.1.1` is the accepted first stable release and genuine two-feed installed
consumer baseline. Its immutable history is preserved. The proposed connected
runtime correction advances the coherent set to `0.2.0`.

The shared WASM release set couples `FS.GG.Wasm.Contracts`,
`FS.GG.Wasm.Browser` and `fsgg-wasm-sdk-0.2.0.tar.gz` at stable version `0.2.0`
under a new immutable tag `wasm/v0.2.0`. The two NuGet packages and the SDK source
archive are one release set. The existing Game `0.16.0` and Game Skills release
axes remain independent.

`eng/wasm-shared/version.props` is the package version origin. The SDK version,
Cargo metadata, compatibility profile, Fable dependency and clean consumer pin
must agree. `scripts/wasm-release/prepare.sh` packs the two explicit projects and
builds the SDK archive once, then freezes their hashes, source commit/tree,
roster, tool versions and source workflow identity in a closed manifest. The
Browser nuspec must depend on Contracts at exact `[0.2.0]`.

The first published baseline remains `0.1.1`, tag `wasm/v0.1.1`, source
`ea015cbf884b01754bc6615476241450907b6b24` and tree
`9526ed555e877080e79b7234b4ec0dac613991ec`. The `0.2.0` custody manifest preserves
that identity and carries the actual SDK10.0.401 ApiCompat comparison against
freshly downloaded published DLLs and their `.fsi` inventories. Contracts remain
compatible. Browser reports four `CP0002` constructor removals: `WorkerCommand`,
`RequestProjection`, `EffectProjection` and `HostProjection` gained fields needed
for validated command configuration, full correlation/operation/phase projection
and compiled/initialized control state. This is an intentionally versioned
pre-1.0 contract migration, not a compatible patch or a first publication.
No ApiCompat suppression is generated. An unexpected diagnostic or unavailable
comparison fails preparation. The actual comparison uses the single-packed
candidate archives; it does not repack between comparison and publication.

The release workflow defaults manual runs to prepare-only. Publication pushes
the retained originals to GitHub Packages first, verifies their readback, uses
NuGet Trusted Publishing OIDC to push the same files to nuget.org, verifies all
non-signature ZIP members, then stages and verifies the SDK/manifest release
assets. Completion publishes the GitHub Release only after every readback.
Recovery accepts retained custody from the same source and version and never
repacks. Availability of package ownership and the two package-specific Trusted
Publishing policies for `release-wasm.yml` remains external readiness evidence.

## Release toolchain: SDK 10.0.403 (FS-GG/.github#4947)

Candidates are prepared and qualified with .NET SDK 10.0.403 (`global.json`,
`wasm-shared.yml`, `prepare.sh`, the supervisor custody scripts and the ApiCompat
receipt label `SDK10.0.403.ApiCompat`). `release_manifest.py` admits exactly two
toolchains: the current `TOOLS` (10.0.403) and `RETAINED_TOOLS` (10.0.401). The
retained one is the immutable identity of every published manifest, `0.1.1` through
`0.7.0`; it stays admitted so published and installed readback keeps verifying them.
A manifest's ApiCompat label must name the manifest's own SDK, and no other SDK is
admitted. Under SDK 10.0.403 the two package archives hold byte-identical members to
SDK 10.0.401 from the same source, the SDK archive is byte-identical, and the
published-API verdict and API surfaces are unchanged. A candidate prepared at `0.7.0`
on SDK 10.0.403 is a qualification candidate only: tag `wasm/v0.7.0` is already
published from SDK 10.0.401 and is not re-cut. The next release version is a separate
release decision.

The distinct feed-only consumer mode accepts HTTPS package and release sources,
creates a lock from remote package bytes, restores a second empty-cache root in
locked mode, downloads and verifies the SDK archive, builds its Rust/C examples,
and exercises the installed package Worker assets below `/sub/app`. It refuses
local custody and package-folder inputs.

The supported boundary is .NET/Fable with the pinned Playwright 1.63 Chromium
profile, BAR ABI 1, strict imported SC2 ABI `0x00010000`, Rust 1.90.0 and WASI
SDK 34. Other browsers, product adoption, native authority and deterministic
fuel or replay remain outside this release wiring.

The immutable `wasm/v0.1.0` tag and its failed publisher run remain historical
evidence. That run failed before packing because browser tool installation
created an untracked `node_modules` directory in the exact source checkout;
no packages or release assets were produced. The corrected `0.1.1` cut installs
browser tools from the locked fixture in an isolated `RUNNER_TEMP` directory.
The `0.1.1` publisher and both fresh installed feed consumers succeeded. The clean
source guard remains mandatory before packing every subsequent cut. Generated
F# Worker policy inputs must live outside the exact source checkout, and the
`0.2.0` cut requires a new tag, retained custody and genuine readbacks.


The read-only installed workflow accepts exact root-verified release version,
published source commit, qualifier commit, manifest SHA-256 and SDK SHA-256.
It executes the current immutable qualifier revision, verifies the native
published release's asset digests, binds its tag and manifest source/tree, checks
consumer/SDK/toolchain provenance against the producer revision, and downloads
and hashes the exact manifest and SDK. Org and public feed jobs run serially with
read-only package access. Unknown hashes and mutable source names are refused;
this route cannot publish or rerun an old publisher.

The installed route is derived from observed state and is not re-pinned per release.
`eng/wasm-shared/version.props` selects the one release that can be qualified; a
dispatch for any other (historical) version is refused with that cause. The publication
identity is the tag-resolved source, which the published manifest must bind through
`release_manifest.py verify-identity` (version, source and tree against Git), together
with the native release-asset digests and the promotion binding. The published tree is
read from Git. Between the published source and the accepted qualifier, every path under
the installed-consumer input closure (`CONSUMER_INPUTS` in `bind-installed-release.py`:
`eng/wasm-shared/`, `src/Wasm.Contracts/`, `src/Wasm.Browser/`, `sdk/wasm/`,
`tests/Wasm.PackageConsumer/`, `tests/Wasm.Supervisor.Compatibility/`) must be byte-identical.
Other changed paths are qualifier tooling or unrelated development and are recorded in
the installed input audit rather than listed per release. On the public feed the restored
package is compared member by member with the frozen inventories after excluding the
repository signature (`.signature.p7s`), by the same content-hash projection as promotion;
whole-file hashes of nuget.org packages differ from the manifest by design. Earlier
installed qualifications (for example `0.6.0`) remain verifiable as history at the
revision and run that produced them.
