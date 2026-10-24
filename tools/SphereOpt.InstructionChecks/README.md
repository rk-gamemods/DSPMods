# SphereOpt instruction checks

This net8.0 console tool exercises the real `ShellCursorRewriter.cs` and
`ColorStoreRewriter.cs` linked from `external/SphereOpt` against captured game
IL. It also checks composition with the reviewed DSPOptimizations 1.1.19 paint
and DrawModels transformations. HarmonyX 2.5.5 and Mono.Cecil 0.11.6 remain
pinned in `packages.lock.json`.

Run from the workspace root in PowerShell. With the existing restored assets
and cached packages, build only the checker, putting generated output under
ignored `.local/`:

```powershell
$project = (Resolve-Path 'tools/SphereOpt.InstructionChecks/SphereOpt.InstructionChecks.csproj').Path
$assets = Join-Path (Split-Path -Parent $project) 'obj/project.assets.json'
$buildRoot = Join-Path (Get-Location).Path '.local/build/InstructionChecks'
dotnet build $project --no-restore --configuration Release --nologo --verbosity minimal `
  "-p:BaseOutputPath=$buildRoot/bin/" `
  "-p:BaseIntermediateOutputPath=$buildRoot/obj/" `
  "-p:ProjectAssetsFile=$assets" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Instruction checker build failed' }
$checker = Join-Path $buildRoot 'bin/Release/net8.0/SphereOpt.InstructionChecks.dll'
$snapshotProps = [xml](Get-Content -Raw -LiteralPath '.local/GameSnapshot.props')
$capturedDll = Join-Path $snapshotProps.Project.PropertyGroup.DSPManagedDir 'Assembly-CSharp.dll'
dotnet $checker --assembly $capturedDll --report .local/build/InstructionChecks/stack-proof.json
```

All options take one value; order is unrestricted and duplicates are rejected:

```text
--assembly <captured Assembly-CSharp.dll>
[--plugin <compiled candidate SphereOpt.dll>]
[--dsp-optimizations <reviewed installed DSPOptimizations 1.1.19 DLL>]
[--report <local JSON path>]
```

The exit code is 0 on success and 1 on failure. Success JSON is bounded to
32 KiB. Keep reports under `.local/`. Optional checks report `not-requested`
when their input is absent; a default run does not establish candidate ordering
or installed/source equivalence.

To validate a candidate built by the integration task, supply its DLL and the
read-only installed DSPOptimizations DLL:

```powershell
$candidateDll = '<path to the compiled candidate DLL>'
$dspoDll = '<path to the read-only installed DSPOptimizations.dll>'
dotnet $checker --assembly $capturedDll --plugin $candidateDll `
  --dsp-optimizations $dspoDll --report .local/build/InstructionChecks/candidate-proof.json
```

To rerun the process boundary checks with those inputs:

```powershell
py -3 tools/SphereOpt.InstructionChecks/run-regressions.py --checker $checker `
  --assembly $capturedDll --plugin $candidateDll --dsp-optimizations $dspoDll
```

The runner requires both optional checker inputs to pass, compares two reports
byte for byte, rejects duplicate options, checks input hashes before and after,
and verifies that the checker output includes the current third-party notice.
Evidence goes to a fresh `run-*` directory under
`.local/build/InstructionChecks/regression/`. The parent folder's
`regression-result.json` summary names that directory and the reports. A missing
new report fails even when old proofs exist. The summary rejects declared-input
path/hardlink aliases before writes, publishes through replacement, and checks
input hashes again after publication. Execution failures after successful preflight publish
`failed`. Preflight failures can leave an earlier summary unchanged, so always
check the invocation's exit status. Run the seven boundary regressions with:

```powershell
py -3 tools/SphereOpt.InstructionChecks/test_regression_runner.py
```

Optional
`--legacy-plugin <pre-fix candidate DLL>` also verifies rejection for missing
ordering metadata. Optional `--upstream-license <pinned source LICENSE>` compares
the copied MIT notice verbatim, allowing only final whitespace differences.

The installed input must match the reviewed package's DLL SHA-256
`721ad22d207eea7ed8a33fe0a5d57682fa715517db5e858a529e4178967f33ba`.
Its assembly version is `1.0.0.0`, which is not the package version.

The captured DrawModels contract requires exactly two precise cursor loads on
the same layer local, feeding the signed remainder and shell loop condition.
The shell index starts at one and increments by one; the loop's false edge
enters the same layer's overlay setup. Paint requires exactly one shell color
store. Field identity and semantic relationships select substitutions; byte
offsets are observations. Missing/extra loads, altered loop semantics, and
foreign same-name fields are negative controls.

After normalizing only the intended substitutions, the checker compares every
opcode, operand identity, label and exception block. It checks input immutability,
independent instruction/list cloning, and the raw Cecil instruction graph,
locals and EH metadata. Labels attach to actual branch/switch destinations;
branch operands remain original Cecil objects. DrawModels requires no EH.
Paint's two disjoint, contiguous try/finally regions supply six actual boundary
markers. Other EH shapes fail closed.

The DSPOptimizations paint checks exercise its actual pinned CodeMatcher
operations with the real linked ColorStoreRewriter. DSPOptimizations first must
retain setter / shell-local load / material-update adjacency. The reverse order
is a required negative control: the failed store match silently appends a brush
`ldarg.0` and material update after the final `ret`. This is an instruction
placement observation, not a JIT or loader result.

With `--plugin`, Cecil reads the compiled paint patch's target, transpiler
marker, ColorStoreRewriter call, owner ID, priority, before and after attributes.
Its actual `HarmonyAfter("com.Selsion.DSPOptimizations")` must be present. These
values feed HarmonyX's real `PatchProcessor.GetSortedPatchMethods` through local
methods that have no Harmony attributes. Both registration orders must put
DSPOptimizations first. Removing only the actual candidate's dependency must
restore candidate-first order in the counterfactual. Class-level ordering and
duplicate/unsupported attribute encodings fail closed. If the installed DLL is
omitted, the other patch uses reviewed source defaults. This checks two-patch
sorting, not the installed loader, other mods, or real patch registration.

For DrawModels, all six permutations combine ShellShaderVarOpt's removal of the
shell material-update call, SphereBufferOpt's replacement of unconditional batch
sync, and the real two-cursor rewriter. The expected edit contract requires the
renderer / batch-index / editor-mask / game-mask operands, two cursor calls,
and preservation of all other instructions, including vanilla node/frame and
overlay code. The two removed sync instructions must have no entry labels or
EH boundaries. The method-reference adapter rejects foreign owners/scopes and
incorrect return types, parameters or static signatures.

SphereBufferOpt's `EmitDelegate` expression is replaced with a static throwing
fixture-call instruction consuming the same four stack arguments. No delegate
is generated and the callback never runs. With the installed DLL, Cecil also
observes the callback's `layersDirtyMask`, `batches`, `renderPlace`, and
`SyncBufferData` references. Callback behavior, dirty-field injection, preloader
success and emitted IL validity remain untested.

Optional installed/source equivalence compares all 58 paint and 32 shell-call
removal instructions, signatures, local types and EH. It normalizes nops, short
opcode forms and branch destinations, omits type/assembly scopes, and maps the
explicit fixture type names. SphereBufferOpt's matching/removal and first two
inserted mask loads also match a 44-instruction prefix, with a matching
5-instruction return suffix. Its delegate factory and callback are excluded.
The check does not assert whole-binary/source or callback equivalence. The
report retains normalized-body hashes, input DLL SHA-256/MVID,
dependency hashes and hashes of the source bytes embedded at checker build time.

Adapted DSPOptimizations source is pinned to
`d46a01cb5db971fc7935e0fb13f172d367b96638`:

- `ShellShaderVarOpt.cs`: SHA-256 `0b5876b14626abe5e8b0d7c3cd5824a477453f4237058e95e4bf0e996fcd957e`.
- `SphereBufferOpt.cs`: SHA-256 `66b2c6f3fbf3f58f3dc1df4956087e4eb61d944e46da64f1f6687739dfa523ba`.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for attribution, the MIT
notice and the exact adaptation boundary.

The adapters map only precisely validated field references and two captured
parameterless instance-void method references to local reflection metadata.
Fixture fields and methods are not game runtime types and establish no ABI
compatibility or production replacement behavior. Other operands retain their
original Cecil identity. The label generator defines labels without emitting
instructions or compiling a delegate.

Cecil refuses external assembly resolution. Assembly-load observation rejects
`Assembly-CSharp*`, `UnityEngine*`, `BepInEx*`, `SphereOpt`, `DSPOptimizations`,
`*Weaver*`, and `*BuildToolOpt*`. No Harmony patch is applied, and no game/plugin
code, rendering or graphics performance is tested.
