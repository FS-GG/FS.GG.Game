# C3-GAME-01 — Ordinary V2 receiver adoption

Status: activation candidate prepared. Protected merge, first settlement, Authority readback, and
normal already-complete rerun remain pending.

FS.GG.Game is the fixed C3 source repository (`FS-GG/FS.GG.Game`, repository ID
`1290990429`) under the code-owned `game-v1` profile. The activation change binds the prepared
receiver to the public CLI 0.1.5 archive and Game's dedicated `ordinary-v2` custody. It changes no
repository setting, environment policy, branch protection, required check, generated workspace
content, or V1 behavior.

## Activation candidate

- The receiver remains bound only to protected-main pushes. A secret-free preflight emits a
  same-run receipt and activation bit; only that exact receipt can admit the environment-bound
  settlement job. Both checkouts persist no credential, and all GitHub permissions remain read-only.
- The source pattern comes from Governance receiver commit
  `afa7a965f1c908195bd57c9d9e52d18669edae7e`; the observer retains its repaired Audio bytes,
  and the qualifier replaces only the code-owned source profile. Their SHA-256 digests are
  `6e63f6f724fb267e77ef02f41b4c58a833e0dc5b08c003f07fd16c89e4f7fd55` and
  `642451c72ff01e51c5339f4e42d5fc1ffe3df39a4f965610cc7621601a06b8b9` respectively.
- The selected settlement checks are `Deterministic gate (locked restore + build) (ubuntu-latest)`
  and `Full test suite (dotnet test, headless) (ubuntu-latest)`. All 19 separate native required
  gates remain required, including the Windows deterministic build, full tests, and property
  invariants, plus the Ubuntu property invariants and existing source, skill, lint and receiver gates.
  The policy records every exact check name. Every check is bound to
  GitHub Actions App `15368` and its exact workflow ID,
  path, event, PR head, run, job, suite, and attempt.
- Both selected checks come from Game's existing gate workflow `308011589` at
  `.github/workflows/gate.yml`; the other producer workflows are coordination coherence
  `308011587` and kit materialization `316870217`. The existing `routine-eligibility`
  producer remains part of Game's source-delivery route; it is neither one of the 19
  branch-required gates nor a selected `game-v1` settlement check. This receiver leaves it unchanged.
- The shared policy ID remains `v2-ci-i1-ordinary-settlement-v1`; the shared Authority anchor retains
  App `5064713`, installation `164553252`, repository `FS-GG/FS.GG.Coordination.Authority`
  (`1351660651`), `contents:write`, metadata read, and the existing writer/integrity ruleset pins.
- Fresh readback against Game main `ec55a10ad5e458944af02c43f48fedeb4b24a430` found
  `ordinary-v2` environment ID `22921368593`, restricted to the single `main` branch policy ID
  `61289298`, with no reviewers and exactly the three dedicated secret names. Custody bridge run
  `36437875033` verified the signed fixed-destination packet against the empty inventory before
  exactly three encrypted PUTs; an independent names-only readback confirmed the final inventory.
- Coordination CLI `0.1.5` is published from source
  `1268908d2d5a38d30a764c927f3e0591e53138aa`. The GitHub release archive is pinned by SHA-256
  `3567a92825917a7d537f6c5c545d3a7947bc35edd666fc3a1898de3bf97267c9`; dual-feed payload
  readback and an independent public-only install passed.
- Game already pins .NET SDK `10.0.401` in `global.json`. The credential job uses that pin,
  downloads only the named GitHub release asset, verifies its bytes, constructs a local-only package
  source, and executes exactly one `game-v1` settlement attempt with the three dedicated secrets.

## Remaining protected boundary

The receiver becomes active only if this exact candidate passes Game's native checks and is merged to
protected main. The first push run must then establish all of:

1. the secret-free preflight accepts the exact merge and current native check producers;
2. the environment job rechecks that receipt, the installed policy and the shared Authority binding;
3. installed CLI 0.1.5 appends one valid settlement to the protected Authority journal; and
4. independent readback verifies the journal entry, followed by one normal rerun that reports the
   operation already complete without moving the journal head.

Failure at any step remains local to Game and is repaired forward. This activation imports no V1
admission or receiver state, leaves `OpenV2` unchanged, and makes no fleet-wide completion claim.
