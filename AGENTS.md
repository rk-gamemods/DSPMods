# Dyson Sphere Program workspace

This repository owns DSP analysis tools, validation contracts and research.
Its workflow covers private game-code capture, indexing, offline assessment and
publication of workspace-owned tools and research, and the requested current-game
SphereOpt source update. Deployment, game-file edits and game launches remain
outside this workflow unless explicitly requested.

## Boundaries

- `external/SphereOpt/` is an independent, ignored Git clone. Inspect its own
  status before changes. Preserve its upstream source and notices.
- The current update retains vanilla node/frame drawing. Do not activate legacy
  replacement shaders or culling merely because their C# source compiles.
- Keep the canonical Unity-project shell source, delivered bundle, runtime hash
  pin and `SHADER_PROVENANCE.json` synchronized. Run the shader identity check.
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
- `tools/prepare_code_index.py` creates a verified source copy under `.local/`
  with independent Git identity for indexing. It must have no remote. Never
  publish or stage that private source tree.
- Follow the [SphereOpt indexing workflow](README.md#index-sphereopt-source-and-project-text)
  for the mod clone. Default JCodeMunch discovery omits its shader extensions.
  Completion requires a file inventory and hash-verified text readback covering
  every implementation and text file, recorded in the local coverage manifest.

## Commands

See [README.md](README.md) for setup and validation. Use explicit interpreters.
Run focused Python capture tests and the offline checker's self-test before
committing changes to their behavior. Test unchanged-input capture reuse.

## Scope and history

The workspace and SphereOpt have separate histories. SphereOpt's clone retains
its original remote; publishing the workspace does not publish or fork that
clone. Read [licensing notes](docs/LICENSING.md) before distributing code.

Research memories use the topic `dspmods_sphereopt_assessment`.
