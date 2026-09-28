# C3-GAME-01 — Ordinary V2 receiver adoption

Status: source prepared and disabled. CLI release, custody, installation, and activation remain pending.

FS.GG.Game is the fixed C3 source repository (`FS-GG/FS.GG.Game`, repository ID
`1290990429`) under the code-owned `game-v1` profile. This change adds only repository-owned
receiver source. It changes no repository setting, environment, secret, branch protection, required
check, generated workspace content, or protected effect.

## Prepared source

- The receiver workflow is bound to protected-main pushes, but its only job has an unconditional
  false guard. It uses read-only GitHub permissions, persists no checkout credential, and contains
  no credential job, environment binding, secret reference, package download, or settlement command.
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
- Read-only API observation at `2026-09-28T12:08:12Z`, against Game main
  `9b76ecab1bf240643d5b81aaed9b9b9861177446`, found `ordinary-v2` environment ID
  `22921368593`, restricted to the single `main` branch policy ID `61289298`, with no reviewers
  and zero secrets. No credential is enrolled.
- No immutable published CLI release with `game-v1` support is selected. Version and package
  SHA-256 remain null, and policy explicitly refuses activation rather than borrowing Audio's pin.
- Game already pins .NET SDK `10.0.401` in the repository's tracked `global.json`. This
  receiver leaves that pin unchanged and invokes no .NET setup while disabled.

## Installation boundary

Do not enable the preflight or add a credential job until one reviewed source change verifies all of:

1. an immutable published Coordination CLI supports the exact `game-v1` source profile and its
   served package SHA-256 is pinned;
2. all three dedicated ordinary-v2 credentials are enrolled and independently read back without V1
   or callable-operation credential reuse; and
3. Game identity, exact current required-check population, producer mappings, and shared
   Authority binding are freshly read back.

The later activation must change policy status, installed state, package evidence, credential
inventory, observer guard, and the bounded credential job together. This disabled source cannot
settle work and imports no V1 admission or receiver state.
