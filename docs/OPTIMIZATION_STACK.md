# Installed DSP optimization stack

Reviewed 2026-10-24 against captured DSP `0.10.35.29151`, Steam build `25955035`,
Unity `2022.3.62f3c1`, and the SphereOpt candidate at
`7a566ddad93ab58529c02fa3778d929eb747aa85`.

The four mods have complementary work, but their gains are not independent
multipliers. Weaver already owns broad factory simulation and scheduling work.
DSPOptimizations still contributes vanilla node/frame synchronization and other
targeted changes. BuildToolOpt mainly addresses construction and blueprint
operations. SphereOpt's shell batching, repeated render-state reconciliation and
proposed partial shell-buffer uploads remain a distinct area to improve.

Compatibility for our modified SphereOpt needs additional proof: this review
identified a paint-transpiler ordering hazard with DSPOptimizations. The earlier
SphereOpt offline acceptance did not test this combined stack.

## Installed inputs and source pins

The user reports all four mods in use. The local mod-manager `Main` profile lists
the following packages as enabled. Package manifests agree with those versions.
This is installation evidence, not a fresh observation of a running process.

| Mod | Enabled package | Reviewed source |
| --- | --- | --- |
| Weaver | `Loom-Weaver 2.5.0` | [HEAD `55fcd2cf`](https://github.com/TheAIBot/DSP_Weaver/tree/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3); [2.5.0 publication commit `891de047`](https://github.com/TheAIBot/DSP_Weaver/commit/891de047e65c27f89c6b4b9db4a2576eefa642ed) |
| DSPOptimizations | `Selsion-DSPOptimizations 1.1.19` | [`d46a01cb`](https://github.com/Selsion/DSPMods/tree/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations) |
| BuildToolOpt | `starfi5h-BuildToolOpt 1.1.8` | [Release commit `fdb6e357`](https://github.com/starfi5h/DSP_Mod/tree/fdb6e3573d41f78b3443d0c6021211d48585c956/BuildToolOpt); identical subtree at reviewed monorepo HEAD `eae28f56` |
| SphereOpt | `Andy-SphereOpt 0.9.2` | Installed upstream DLL identity matches the [baseline assessment](SPHEREOPT_ASSESSMENT.md); unofficial local candidate is 0.9.3 |

The candidate package has not been deployed. The profile's available load log
was last modified on 2025-02-12 and names older mod versions. Configuration
headers also name older versions. Neither is current loader/version evidence.

Installed DLL SHA-256 values:

| DLL | SHA-256 |
| --- | --- |
| `Weaver.dll` | `26c20109c224c762105eba0087271cafe8aa4aee9adf575dfb9587eadfd43113` |
| `DSPOptimizations.dll` | `721ad22d207eea7ed8a33fe0a5d57682fa715517db5e858a529e4178967f33ba` |
| `BuildToolOpt.dll` | `3240d870c51f9f7f0708ada65a721b515ee1d02df049b2d6bf8b8a1a5328efde` |
| `SphereOpt.dll` | `1af8872de34751a4d6f1a9f42aa9dc1100e189406b6207697c646d9f0b3616d0` |

Weaver's installed DLL exactly matches the repository's committed
`Publish_Weaver/Weaver.dll`. Metadata-only decompilation confirms plugin version
2.5.0. Its assembly informational version mentions the older `d887a294` commit;
that string is not an attestation that its code equals that commit. The installed
DSPOptimizations plugin declares 1.1.19 despite its generic 1.0.0.0 file version.
Its preloader is present, with SHA-256
`f8d41d1b781cfd60952cfa06f645d66dd4ac6bdae80b3d5eb3cc5b3b88c4332b`.
Presence does not establish successful preloader or Harmony installation.

The ignored local evidence includes `optimization-installed-profile.json`,
per-mod review reports and isolated upstream research clones under
`.local/research/`. No installed plugin was executed. These new clones were
reviewed directly; no complete Munch indexing claim is made for them.

## Weaver: large simulation changes, conditional headline gain

The author's [performance claim](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/readme.md#L1-L12)
is roughly 3.0-3.5 times performance in their late-game saves, with the best
results in outer space. The inspected evidence does not define the headline
metric as FPS, UPS or frame time, or supply a reproducible baseline, save,
hardware, exact configuration and repeated raw results. Treat it as an author
report whose applicability depends on the workload.

The implementation provides a substantial CPU mechanism behind that claim:

- [Factory executors](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/OptimizedTerrestrialPlanet.cs#L72-L196)
  use compact representations, shared recipe/static data and connected
  sub-factories to reduce unnecessary work and memory indirection.
- The [scheduler](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/OptimizedStarCluster.cs#L151-L258)
  replaces `GameThreadController.LogicFrame`; workers distribute independent
  planet/sub-factory work. The local planet still receives Weaver scheduling
  and parallel work even when it uses the unoptimized component representation.
- [Dyson optimizations](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/OptimizedDysonSphere.cs#L52-L269)
  cache sphere power and change construction handling. Sail/rocket updates also
  run in [worker tasks](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/WorkDistributors/WorkChunks/DysonSphereAttach.cs#L20-L38).
  Statistics and remote-planet presentation receive additional optimizations.

The [2.5.0 changelog](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Publish_Weaver/CHANGELOG.md#L1-L10)
adds scheduling concurrency between transport/assemblers and cargo preparation/
combat. HEAD also contains prospective 2.6.0 belt-data work. Do not attribute
that unreleased work to the installed 2.5.0 DLL merely because source version
constants still say 2.5.0. Its project references DSP 0.10.35.29088 and an older
Unity module set, rather than the exact captured build.

### What compatible means here

The author explicitly lists SphereOpt and DSPOptimizations as compatible. In
[issue #27](https://github.com/TheAIBot/DSP_Weaver/issues/27#issuecomment-3537618179),
TheAIBot also says Weaver is not intended to obsolete DSPOptimizations and is
unlikely to replace SphereOpt's shader work. That supports keeping the mods
together as a design direction. No named adapter, version gate or inter-mod
Harmony ordering rule for those two was found in the reviewed Weaver source.
The statement supplies neither an exact version matrix nor validation of our
unofficial SphereOpt 0.9.3.

Weaver can bypass original component methods that other mods patch. Its
[compatibility warnings](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/WeaverFixes.cs#L29-L77)
identify this mechanism; warning popups do not enforce exclusions. The sole
live user option found, `WarnAboutModIncompatibility`, controls that popup.
In [issue #31](https://github.com/TheAIBot/DSP_Weaver/issues/31), the author
addressed local-planet parallelism after a reported slowdown. It demonstrates
workload sensitivity, without establishing a current regression.

## DSPOptimizations: inspect active registration, not the old checklist

Source [registration and patch installation](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/OptimizationSet/PatchManager.cs#L13-L30)
select the following relevant families:

| Active family | Scope | Relationship to SphereOpt |
| --- | --- | --- |
| [ShellShaderVarOpt](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/Optimizations/ShellShaderVarOpt.cs#L11-L103) | Moves vanilla shell material updates from every draw to construction/state/painting hooks | Related purpose, different storage; also shares paint and draw patch targets |
| [SphereBufferOpt](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/Optimizations/SphereBufferOpt.cs#L18-L85) | Dirty-mask synchronization for vanilla node/frame prototype batches | Complements the candidate's retained vanilla nodes/frames; does not upload SphereOpt shell buffers |
| [DysonNodeOpt](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/Optimizations/DysonNodeOpt.cs#L120-L229) | Maintains construction totals and computes layer power from them | Overlaps Weaver's power-cache family; outside SphereOpt rendering work |
| [MonitorOpt](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/Optimizations/MonitorOpt.cs#L15-L156) | Circular traffic-monitor history, serialization/UI reconciliation | Distinct from shell rendering; Weaver may use different execution paths |
| [LayerDismantleOpt](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/Optimizations/LayerDismantleOpt.cs#L19-L52) | Consolidates model rebuilds during bulk layer removal | Separate lifecycle optimization that combined removal tests must retain |

The [1.1.19 changelog](https://github.com/Selsion/DSPMods/blob/d46a01cb5db971fc7935e0fb13f172d367b96638/DSPOptimizations/changelog.txt#L1-L15)
disables station-storage threading after vanilla implemented it, disables sail
bullet optimization and removes the obsolete stats fix. Earlier releases
removed node sail-absorption and color work. Retained prototypes and old README
performance percentages are not additional active benefits.

Monitor work is also path-dependent. Weaver's optimized
[monitor executor](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/Monitors/MonitorExecutor.cs#L20-L41)
calls its own implementation; its
[local/unoptimized work chunk](https://github.com/TheAIBot/DSP_Weaver/blob/55fcd2cf7461a29abd4f911eaa1fff63ee2b33a3/Optimizations/WorkDistributors/WorkChunks/UnOptimizedPlanetWorkChunk.cs#L910-L926)
calls vanilla `MonitorComponent.InternalUpdate`, where DSPOptimizations patches
can still apply. Installation alone does not make every optimization contribute
on every planet.

The observed configuration has `disableShadows=false`, so this profile does not
request that visual tradeoff. Its old `EjectedSailsVisible=true` entry belongs to
the now-disabled sail class. No live per-family switch was found for disabling
the active shell paint patch. The author
[reported 1.1.19 fixing earlier patch failures](https://github.com/Selsion/DSPMods/issues/11#issuecomment-3514815976);
that statement concerns earlier game versions, not a certification of this build.

## BuildToolOpt: construction, blueprint and optional route work

[BuildToolOpt 1.1.8](https://github.com/starfi5h/DSP_Mod/blob/fdb6e3573d41f78b3443d0c6021211d48585c956/BuildToolOpt/src/Plugin.cs#L33-L104)
removes explicit placement-time garbage collections, optimizes blueprint
operations and provides station replacement, spacing indicators and input/UI
features. Removing forced collections does not disable automatic GC or eliminate
allocations. Its shortcut hooks still run in recurring player update methods,
with useful actions gated by input/mode.

The observed profile explicitly enables `RemoveGC`, `ReplaceStation`,
`UIBlueprintOpt` and `ClipboardPaste`; it disables `GC when pause` and
`EnableHologram`. Newer keys absent from the old config file use startup
defaults, subject to successful plugin initialization. In particular,
`EnableStationBuildOptimize` defaults to **false**. Do not count its optional
[route/removal optimization](https://github.com/starfi5h/DSP_Mod/blob/fdb6e3573d41f78b3443d0c6021211d48585c956/BuildToolOpt/src/Optimization/GalacticTransport_Patch.cs)
as an enabled benefit just because the mod is installed.

That optional feature reconciles logistics pairings and vessel state during
station/route changes. It is not a steady-state factory scheduler or a shell
renderer optimization. No Weaver/SphereOpt/DSPOptimizations adapter or named
ordering rule was found in BuildToolOpt. A historical building-crash report
that listed BuildToolOpt was reproduced without it and
[fixed in Weaver 2.0.1](https://github.com/TheAIBot/DSP_Weaver/issues/20#issuecomment-3393110313);
it does not establish a current BuildToolOpt conflict.

There is feature-specific evidence on the exact game version:
[issue #101](https://github.com/starfi5h/DSP_Mod/issues/101) names DSP
0.10.35.29151, and starfi5h reports testing the mining-shortcut switch.
The actual key is `EnableUIMiningMachineKey`; the README omits `Enable`.
This supports that particular feature, not the full stack. The older optional
replacement/hologram interaction in
[issue #69](https://github.com/starfi5h/DSP_Mod/issues/69) remains unresolved;
holograms are disabled in the observed configuration.

## Overlap and remaining SphereOpt work

| Work | Existing owner/evidence | Decision |
| --- | --- | --- |
| Factory data layout, sub-factory scheduling and worker distribution | Weaver | Avoid recreating inside SphereOpt |
| Dyson power totals and parallel sail/rocket logic | Weaver, partly overlapping DSPOptimizations | Avoid adding another implementation; assess combined execution before attributing gains |
| Vanilla node/frame upload suppression | DSPOptimizations | Preserve vanilla path and validate combined draw transpilers |
| Construction GC, blueprint processing, optional route refresh | BuildToolOpt | Keep out of SphereOpt scope |
| Batched shell drawing and owned shell buffers | SphereOpt | Remains distinct |
| Repeated SphereOpt shell/node reconciliation and allocations | SphereOpt candidate | CPU improvement opportunity after compatibility proof |
| Progress/appearance changed-range uploads | SphereOpt candidate | Not implemented by the other reviewed mods; retain existing allocation policy |
| GPU capacity reuse and all visibility culling | Deferred | Preserve the user's deferral; outer-layer presence is not occlusion evidence |

Power caching is a concrete example of overlap rather than a multiplicative
gain. Weaver can skip the original `DysonSphere.BeforeGameTick` while its cache
is valid; DSPOptimizations changes the original body. Its body can still matter
when Weaver requests recalculation. The captured current vanilla method already
returns early when `needRecalculatePower` is false, so the baseline itself avoids
an unconditional per-tick scan. This does not establish that both mod caches are
obsolete; it makes workload and actual execution paths essential to attribution.

Lower shell-render CPU cost can also improve simulation/UPS when it frees shared
CPU time, cache or memory bandwidth. No FPS-only limit or simulation-speed gain
is established by this review. Simple addition or multiplication of advertised
speedups would be unsupported.

## Combined patch risks to resolve first

DSPOptimizations' brush transpiler searches for `stfld DysonShell.color` and
uses relative offsets to insert a material update. Our candidate's semantic
color rewriter replaces that store with a setter call. If our rewriter runs
first, that exact match disappears. Neither side currently declares a named
ordering contract for the pair.

A metadata-only executable check reproduced both orders over the captured
789-instruction brush method. It linked the real candidate rewriter and used
the DSPOptimizations transformation body with fixture field/method references:

- **Candidate then DSPOptimizations:** the missing match does not throw.
  Relative matcher movement copies brush `this` and appends a material-update
  pair after the final `ret`, rather than after the color setter. That is an
  observed unintended transformation, not an observed game crash or proof of
  emitted-IL validity.
- **DSPOptimizations then candidate:** preserves the setter and the correctly
  placed shell-local load/material-update call. All other captured instructions,
  labels and exception boundaries remain intact under the tested adaptation.

The installed DSPOptimizations transpiler has 58 normalized instructions, all
matching the copied source body. Six assertion groups passed, and a repeat
produced nine byte-identical artifacts with unchanged input hashes. Private
evidence is `.local/research/stack-transpiler-proof.md` and its adjacent probe
directory. The probe does not select or inspect real installed Harmony ordering,
emit the patched game method, or run its graphics effects. `DrawModels`
composition was not tested. Resolve the ordering/integration contract and add
regression coverage before calling the candidate compatible with this stack.
This is not evidence of failure in the user's installed upstream 0.9.2.

Weaver also replaces `ConstructSp` and calls construction from worker tasks.
Future event-driven readiness/dirty-range work must observe replacement paths
and establish mutation/thread ordering; a hook inside a skipped vanilla body
can miss changes. Broader caching must retain current identity checks and
fallback until invalidation coverage is demonstrated.

The next implementation order is therefore: establish paint/draw composition,
then reduce repeated SphereOpt CPU lookups/calculations/allocations, then add
bounded progress/appearance uploads with full-upload fallback for replacement,
compaction and retries. Keep allocation policy and culling unchanged.

## Validation and limits

This review inspected pinned source, active registration, comments, selected
author issue discussions, upstream release metadata, installed package records,
DLL hashes and selected metadata-only decompilation. BuildToolOpt's 15 C# files
were reviewed; DSPOptimizations scope included its relevant preloader; Weaver
scope included its entrypoint, scheduler, executors and Dyson work. The private
reports record bounded issue-search coverage and unresolved source/version gaps.

No game process, shader, mod DLL or Harmony detour was executed. No game files,
installed packages or configuration were changed. No new in-game benchmark or
complete current-stack certification was produced. Earlier SphereOpt isolated
test counts remain historical evidence for that candidate, not a four-mod test.
