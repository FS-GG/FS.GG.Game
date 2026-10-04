# No-publication release observation

The existing `release.yml` workflow accepts `admission_only=true` on manual
dispatch. This runs a bounded five-minute observation job at the selected source
ref and skips both the full release verifier and the publisher. It does not need
a release tag, pack, restore, or package upload. Leave `source_run_id` empty.

The job reads both registries using the actual repository workflow token, checks
the known Core 0.16.0 predecessor to distinguish registry authentication from an
unqualified 404, and refuses a visible 0.17.0 collision or unavailable response.
Registry absence means absent to that principal; it does not prove permission
to create a package or reveal unrelated private packages.

It then uses the same pinned NuGet/login action and account selection as the
publisher. This exchanges the workflow identity for a temporary credential; it
is **not a credential-free dry run**. No credential is written into observations
or printed by the observation script. The action masks its credential output.
Authenticated GET `api/v2/verifykey/{id}` observations check existing package
authority. Cross-host redirects strip credentials. Only sanitized status,
source/run, time and limitation data become artifacts.

A successful observation is deliberately limited. NuGet's exchange response
contains token type, expiry and key, with no scope readback. Its verify-key route
looks up a package before evaluating its scope; a missing adapter identity
returns 404 without testing new-package creation permission. Therefore the report
always marks adapter creation scope **not exposed and unproven**. It must not be
interpreted as full publication authorization. The account policy roster is a
sign-in-protected UI; the GitHub OIDC exchange does not authorize that UI read.

These boundaries were checked against [NuGetGallery source at
63b66c98287c61c7e00fe6aefaff86c0d73ba157](https://github.com/NuGet/NuGetGallery/tree/63b66c98287c61c7e00fe6aefaff86c0d73ba157):
`TokenApiController.ApiKeyJson`, `ApiController.VerifyPackageKeyInternalAsync`,
and `UsersController.TrustedPublishing`. [Current NuGet documentation](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
supports new packages through policy scopes and globs. Platform capability is
distinct from the effective account's scope.

This source change creates no mandatory manual approval or owner-policy-export
gate. It provides a machine route to observe what the existing interfaces expose.
It cannot create missing trust or credentials. The ordinary stable publication
still uses GitHub-first, one retained coherent package set, OIDC public push,
same-byte readbacks and authenticated retained-byte recovery. No release or
successful provider observation is claimed by adding this route.

Offline qualification:

```sh
python3 tests/release/test-release-admission.py
tests/release/test-game-0-17-release.sh --source-only
tests/release/test-game-0-17-release-selftest.sh
```

The offline controls cover visible collisions, unauthorized/unavailable registry
responses, predecessor authentication, missing-identity scope limits, credential
redirect stripping, and isolation from publication. They do not contact providers.
