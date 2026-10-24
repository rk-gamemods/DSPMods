# Dyson Sphere Program workspace

This repository owns DSP analysis tools, validation contracts and research.
The current task establishes the workspace and offline workflow. It does not
authorize deploying mods, changing game files, launching DSP or changing
SphereOpt's behavior.

## Boundaries

- `external/SphereOpt/` is an independent, ignored Git clone. Inspect its own
  status before changes. Preserve its upstream source and notices.
- `.local/` holds private game assembly snapshots, decompiled source, reports
  and machine-specific metadata. Never commit or publish these inputs.
- `sources.json` records the upstream revision used for this assessment.
- `tools/game_snapshot.py` owns capture identity, staging, verification and the
  latest-snapshot pointer. Do not hand-edit its generated manifests.
- `tools/OfflineChecks/` inspects assembly metadata without executing game or
  plugin code. A passing report is not a runtime or graphics-performance test.
- Game inputs are read-only. Builds use a verified captured assembly set.
- Keep generated output and source indexes local. JDocMunch embeddings stay
  disabled. Prefer configured Munch retrieval after checking index freshness.

## Commands

See [README.md](README.md) for setup and validation. Use explicit interpreters.
Run focused Python capture tests and the offline checker's self-test before
committing changes to their behavior. Test unchanged-input capture reuse.

## Scope and history

This local repository has no remote. SphereOpt's clone retains its original
remote; creating a hosted fork or publishing this workspace is a separate
operation. Read [licensing notes](docs/LICENSING.md) before distributing code.
