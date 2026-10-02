# FS.GG.Wasm.Browser

Unpublished stable shared browser WASM runtime candidate for FS.GG.Game. Version
`0.1.0` contains the typed runtime plus package-owned module Worker assets.
The build-transitive package boundary copies those assets under
`_content/FS.GG.Wasm.Browser/`; `worker-client.mjs` resolves its sibling Worker from
the deployed asset base, including non-root applications. F# retains admission,
lifecycle and settlement policy; the JavaScript assets perform browser mechanics.

This candidate is outside the Game solution and release set. It is unpublished and
has not been adopted by a product.
