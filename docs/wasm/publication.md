# Shared WebAssembly release set

The shared WASM release set couples `FS.GG.Wasm.Contracts`,
`FS.GG.Wasm.Browser` and `fsgg-wasm-sdk-0.1.0.tar.gz` at stable version `0.1.0`
under immutable tag `wasm/v0.1.0`. The two NuGet packages and the SDK source
archive are one release set. The existing Game `0.16.0` and Game Skills release
axes remain independent.

`eng/wasm-shared/version.props` is the package version origin. The SDK version,
Cargo metadata, compatibility profile, Fable dependency and clean consumer pin
must agree. `scripts/wasm-release/prepare.sh` packs the two explicit projects and
builds the SDK archive once, then freezes their hashes, source commit/tree,
roster, tool versions and source workflow identity in a closed manifest. The
Browser nuspec must depend on Contracts at exact `[0.1.0]`.

The first stable cut has no published same-package ApiCompat baseline. Its
committed `.fsi` files and complete package inventories become the `0.1.0`
baseline. Later releases must compare with the last published version; absence
of that baseline is not a successful compatibility comparison.

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

These files prepare source only. No tag, package, release asset or feed version
exists until the protected publication workflow and readbacks succeed.
