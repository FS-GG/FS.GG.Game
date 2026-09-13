# SVG-PRESENT-01.4 — Versioned save and migration authority

Status: implementation complete in this routine change; browser persistence remains owned by Rendering
milestone SVG-PRESENT-01.5.

Game.Core now owns portable compatibility identity for project documents, asset manifests, game saves and
workspace preferences. Each envelope binds engine, profile, schema, content, asset and payload identities.
The migration reducer accepts adjacent schema steps only, exposes one durable step at a time and advances its
recovery point only after the host confirms that step. The autosave reducer separately owns bounded debounce,
one in-flight write, retry, cancellation, replacement generations and disposal.

## Completed acceptance

- [x] Malformed identities, family/engine/profile/schema/content/asset mismatches, corrupt payload hashes and
  unknown newer schemas refuse while retaining the last committed envelope.
- [x] Migration plans refuse skipped, duplicate and non-adjacent steps; product migration failures never emit
  a persistence effect.
- [x] Every successful migration step receives a generation-scoped operation ID. Stale, duplicate and
  post-replacement completions cannot advance or overwrite the recovery point.
- [x] Autosave keeps the last committed value across quota/database failure, retains a newer edit during an
  in-flight write, and retries the newest draft exactly once.
- [x] Cancellation, replacement and disposal invalidate owned work and make late observations inert with an
  explicit refusal.
- [x] Game.Core's exact Fable view includes the canonical `SaveMigration` implementation. Packed .NET and
  Fable consumers emit the same transition corpus at SHA-256
  `80c5b00a6796f4af649f8de0ae7b80e97575fd04067956b9d9896753c7f21268`.

The browser host will supply payload hashing, IndexedDB transaction results and archive validation. The pure
authority layer does not touch clocks, storage, files, DOM APIs or network APIs.
