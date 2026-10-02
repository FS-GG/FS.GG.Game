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
