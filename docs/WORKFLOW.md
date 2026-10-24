# Task scope and workflow contract

## Requested outcomes

1. Register a local T3 project and keep its work outside the scratch directory.
2. Retain SphereOpt source/history and evaluate clone/fork and license options.
3. Capture installed DSP game code for source review and offline mod checks.
4. Update the configured JCodeMunch, JDocMunch and JDataMunch installations.
5. Verify the results and preserve unresolved limits explicitly.

## Ownership

The workspace owns its tools and documentation. SphereOpt is a separate clone.
Installed game files are read-only inputs. Global Munch packages/configuration
belong to the user environment, not this repository. Existing unrelated
repositories, active MCP processes and their working changes are preserved.

T3 registered this workspace at `DSPMods`. The exposed T3 thread metadata tool
does not support moving an existing thread between projects. Use the app's
conversation menu to attach this conversation; creating a project alone does
not move it.

## Capture contract

Inputs are the installed managed DLL set, sanitized Steam build metadata,
capture schema and pinned ILSpy version. Their hashes determine snapshot
identity. Every captured input is re-hashed after copying; installed inputs are
checked again before publication to detect an update during capture.

Output is staged under `.local/staging/`, verified, and renamed to its immutable
snapshot directory. `latest.json` is replaced atomically only after success.
The output manifest covers generated source as well as input assemblies.
An existing matching snapshot is reused only after verification. Corrupt
snapshots are refused rather than silently reported as successful.

An OS file lock permits one capture at a time. The game folder is never an
output destination. Failures remove only the current task-owned staging folder;
prior snapshots and the previous pointer remain available. No binaries,
decompiled source or machine-specific paths enter tracked files.

Capture rejects linked staging, snapshot and publication paths. Build properties
are published before the latest pointer; a pointer-publication failure restores
the previous properties. These guards are covered by capture regression tests.

## Offline evidence

- Compilation against captured game DLLs detects source/API mismatches.
- Metadata inspection detects unresolved game/Unity types and members, missing
  Harmony targets, ambiguous overloads and common injected-argument mistakes.
- Negative fixtures must demonstrate that a nonexistent target is rejected.
- Capture tests cover unchanged reuse, input changes, failed decompilation,
  output corruption, linked output paths, publication failure, compiler input
  selection and refusal of an overlapping capture.

These checks do not prove runtime initialization, native Unity behavior,
transpiler correctness, shader-bundle compatibility, visual correctness,
save compatibility or performance. Decompilation is a reading aid; the original
captured assemblies remain the authority for compatibility checks.

## Scope exclusions

No new gameplay or rendering behavior, deployment, game launch, public fork,
release or redistribution of game material is included in this bootstrap.
