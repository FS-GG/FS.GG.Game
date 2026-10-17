# FS.GG.Wasm.Browser

The selected `0.5.0` source candidate prepares a closed module Worker from the
generated F# policy. Published `0.4.0` and its accepted installed evidence retain
their original identities; the new candidate still requires qualification and
coherent publication.
The build-transitive package boundary copies those assets under
`_content/FS.GG.Wasm.Browser/`; `worker-client.mjs` resolves its sibling Worker from
the deployed asset base, including non-root applications. F# retains admission,
lifecycle and settlement policy; the JavaScript assets perform browser mechanics.
The selected Worker is one bundled ESM artifact with no runtime imports. Raw
`policy/` files and the mechanical source remain package build provenance.
`worker-build.json` joins the generated input bytes, pinned build tools, source
commit/tree and actual bundle hash. The fixed npm build lock is included outside
the runtime asset tree.

`createHostTransport(assetBase, { observe, include })` optionally reports bounded
scalar observations. `include` selects the observation scope; excluded events are
counted explicitly. Callbacks must be passive and bounded. Exceptions mark a read
gap while ordinary native effects continue. `DiagnosticStatus()` reports disabled,
unavailable, observed, truncated or read-failed health; it grants no readiness,
authority, complete coverage or successful cleanup. Worker-local elapsed values
and page clock values are separate clocks. Diagnostics never enter the lifecycle
reducer or renew its pre-create deadline.

The normal transport has diagnostics disabled. Observed mode retains the same
request limits and adds real observation overhead. The build uses exact locked
[esbuild](https://esbuild.github.io/api/#bundle), Acorn and ESLint scope tools;
none is a consumer runtime dependency. Current compilation, fresh installed
Worker behavior and budget fit remain required before acceptance.
