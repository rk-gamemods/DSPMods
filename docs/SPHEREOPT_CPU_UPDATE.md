# Unofficial SphereOpt 0.9.3 CPU and compatibility follow-up

This is our local candidate, based on andyoneal's upstream 0.9.2. It is not an
upstream author release and has not replaced the installed 0.9.2 package. The
candidate source identity is recorded in [sources.json](../sources.json).

Reviewed clone commit: `0411bf590edc29e86a78caead151cf1269ef8b64`, tree
`f49f0e813ceaa38626779873ede6c479137cd2aa`. The initial candidate and comparison
baseline remain at `7a566ddad93ab58529c02fa3778d929eb747aa85`.

This pass fixes the paint-patch interaction identified in the
[installed-stack review](OPTIMIZATION_STACK.md) and reduces work within
SphereOpt's shell renderer. It retains structural readiness checks, vanilla
nodes/frames/painting overlays, the repaired shader and its exact runtime pin.
GPU capacity reuse and all culling remain deferred.

## Paint compatibility

The candidate's brush transpiler now declares
`HarmonyAfter("com.Selsion.DSPOptimizations")`. DSPOptimizations must see the
original shell color store before our semantic rewriter replaces it. A relative
priority alone would not express that dependency as directly as the
[named ordering rule](https://harmony.pardeike.net/articles/priorities.html).

The portable instruction checker reads captured game metadata, the compiled
candidate and, optionally, the installed DSPOptimizations DLL. It checks the
actual ordering declaration and combined transformations without loading game,
plugin or Unity assemblies. The bad unsorted order remains a negative control;
it must not become the selected composition. The checker documents its fixture
adaptations and runtime limits in its
[README](../tools/SphereOpt.InstructionChecks/README.md).

## Rendering CPU work

Readiness still checks current sphere/layer/shell identities, registered geometry
arrays, layer parameters and each referenced node. This pass does not cache an
eligibility answer across structural mutations.

`TryReconcileShell` resolves the allocation once for a shell and handles progress,
color and state together. Previously each referenced node performed its own
allocation lookup, followed by separate color/state lookups. Progress validation
and calculation now share `TryGetProgress`, avoiding repeated validation of the
same inputs. Invalid data still rejects batching for that layer.

Each render invocation retains a unique finalizer wrapper. Its layer-reference
array and submission callback are reused through thread-local draw data, retained
up to peak nesting depth. Finalization clears sphere/layer references and masks.
An old finalizer state cannot retire a later draw that borrowed the same data.
The unique wrapper still allocates; this is not an allocation-free renderer.

A comparison on .NET 8.0.32 x64 measured 40 allocated bytes per call, against
192 in the 7a566dd baseline, on five guard/zero-mask paths. Each path warmed
20,000 calls and measured 100,000 calls through cached delegates with fake event
recording disabled. Active submission reuse is checked through actual array,
payload and callback identities. These results do not measure Unity Mono or
native rendering.

## Changed-range uploads

Progress and shell appearance each track one bounding interval. Multiple writes
merge into that interval, including unchanged entries between its endpoints.
Unity's [offset/count upload API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ComputeBuffer.SetData.html)
copies that span. This keeps bookkeeping bounded and avoids a list of tiny
submissions.

- Initialization, buffer growth/replacement and pool compaction still force full
  uploads. Hex/polygon upload and GPU allocation policies remain unchanged.
- The existing public dirty booleans are independent force-full notifications.
  Setting one overrides a pending precise span, including for coordinated direct
  array writes that the tracked setters cannot observe.
- Failed uploads retain pending work for retry. A changed span is consumed only
  after a successful transfer.
- Progress, color, state and style setters skip unchanged values. The style patch
  now uses a tracked setter instead of directly writing the appearance array.

Supported layer mutations, synchronization and cleanup use one lock. That
protects layer bookkeeping; it does not make Unity resource calls valid on
arbitrary worker threads. Direct public pool/flag writes and game-owned input
arrays still require caller coordination. No unnotified write detection is
promised.

The actual-layer API harness compares the simulated whole GPU-buffer contents
against independent full-array snapshots through changes, failures and removal.
For a fixture with one node and one appearance record changing across ten
frames, progress/appearance transfers total 440 bytes in 20 submissions, compared
with 6,960 bytes in 20 full-buffer submissions. Ten unchanged frames submit zero
bytes. These are algorithmic counts, not measured driver or game performance.

## Packaging identity

The package build pins its assembly informational revision to the independent
mod commit. A documentation commit in the outer workspace must not change that
revision or the resulting mod bytes. Source identity is checked before and after
packaging; deterministic ZIP readback verifies the included payload.

## Validation record

| Check | Result |
| --- | --- |
| Verified raw-game build | Zero warnings/errors; DSP snapshot `5faaee93e64501df2b2a123096d70317ebf8c2ec0d7c5309fbd912f47a23f8ff` |
| Mod helper/policy tests | 231 passed |
| Actual layer with deterministic API fakes | 68 cases passed; repeat-two run totals 136 passes |
| Actual render patch/rewriter with fakes and captured-IL case | 98 passed |
| Evidence-gate regression tests | Nine passed; twelve implementation mutants rejected at their named assertions |
| Game/Unity metadata | 368 references, 16 Harmony targets, two instruction contracts, zero errors |
| Actual compiled paint ordering | Harmony sorter selects DSPOptimizations first in both registration orders; removing the dependency restores the bad order |
| Combined captured instructions | Paint composition and all six draw-transformation permutations pass, preserving unaffected instructions, labels and exception markers |
| Installed DSPOptimizations comparison | 58 paint and 32 material-call-removal instructions match; buffer-sync prefix/suffix match, with delegate generation/body explicitly excluded |
| Process/repeat checks | Reports repeat byte-for-byte; duplicate options and pre-fix ordering metadata rejected; source/input hashes unchanged |
| Runner freshness/preservation | Seven boundary tests pass; both stale-output and input-collision reproductions fail on the prior runner and pass after correction |
| Workspace Python regressions | 19 passed |
| Shader identity | Both bundle copies and canonical function hashes unchanged |
| Source patch | Reconstructs tree `f49f0e813ceaa38626779873ede6c479137cd2aa` from the pinned upstream baseline using an isolated Git index |

The three mod suites contain 397 distinct passing cases with no failures or
skips. Metadata adaptations, fake API behavior and the unchanged shader contract
are separate evidence boundaries; the tests do not collapse those boundaries
into a native runtime claim.

Independent review found no remaining confirmed P1/P2 defects in the reviewed
scope. Two earlier runner defects, stale proof reuse and summary/input aliases,
were reproduced and corrected. Fresh run directories and input-preserving
summary publication now have regression coverage. Preflight failures can leave
an older summary unchanged; callers must check the process exit status.

The local package is `.local/packages/SphereOpt-0.9.3-0411bf590edc.zip`, 319,533
bytes, SHA-256
`efef6bea555c88d14a406202ec9afdbc5e01d7173394e0fee7ddd43c180a63db`.
Its DLL is 111,616 bytes, SHA-256
`741fef0bd91d5a6750e32cc9b8c98ffb15f8df06daef0554db6050b36824218c`;
assembly informational version is
`0.9.3+0411bf590edc29e86a78caead151cf1269ef8b64`.

Both mod indexes were refreshed at that exact commit. The complete inventory
contains 129 files: 125 text and four binaries. All 80 implementation files
(62 C#, 15 GPU and three Python) have hash-verified code-body readback. Code
index `andyoneal/SphereOpt` contains 113 files and 1,571 symbols. Text index
`local/SphereOpt-fulltext-0411bf590edc` contains 126 documents and 252 sections;
all full bodies, totaling 1,036,187 bytes, were independently verified. Shader
symbol extraction remains approximate. Embeddings and AI summaries stayed off,
and the required inventory check passed with no omitted scope. The existing
95-assembly game index was unchanged.

## Performance scope and validation limits

This pass targets rendering CPU overhead, managed allocation and upload volume.
Savings can also improve simulation/UPS when rendering and simulation compete
for CPU time, cache or memory bandwidth. The outcome depends on the bottleneck.
No FPS-only limitation or measured UPS gain is asserted.

The game's GPU-profiler switch currently selects the complete vanilla path.
Any future benchmark must verify that batching remains active, use the actual
installed optimization stack, and distinguish completed-sphere steady frames
from construction, painting and deletion workloads.

Builds, fake-API lifecycle tests, managed-host allocation checks, metadata and
captured-IL transformation proofs do not certify native Unity/Mono behavior,
visual fidelity, full-stack Harmony installation, saves, multiplayer or speedup.
No game launch, installation or configuration change is part of this pass.
