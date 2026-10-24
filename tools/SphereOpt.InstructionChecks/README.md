# SphereOpt instruction checks

This net8.0 console tool links the real `ShellCursorRewriter.cs` and
`ColorStoreRewriter.cs` from `external/SphereOpt`. NuGet versions are pinned to
HarmonyX 2.5.5 and Mono.Cecil 0.11.6, with locked restoration after the first
restore. It requires that local clone and a verified captured game snapshot.

Run from the workspace root in PowerShell:

```powershell
dotnet restore tools/SphereOpt.InstructionChecks --locked-mode
$snapshotProps = [xml](Get-Content -Raw -LiteralPath .local/GameSnapshot.props)
$capturedDll = Join-Path $snapshotProps.Project.PropertyGroup.DSPManagedDir 'Assembly-CSharp.dll'
dotnet run --project tools/SphereOpt.InstructionChecks --no-restore -- --assembly $capturedDll --report .local/actual-il-proof.json
```

Exit code 0 and bounded success JSON prove that the captured DrawModels body
has exactly two precise cursor loads on the same layer local, feeding the
signed remainder and shell loop condition, with the loop's false edge entering
that layer's overlay setup. The shell index starts at one and increments by one.
Paint must contain exactly one shell color store. Byte offsets are reported
as observations; field identity and semantic instruction relationships select
the substitutions.

The checker invokes both linked helpers. After normalizing only the intended
two cursor substitutions and one color substitution, it checks every opcode,
operand identity, label and exception block. It also checks input immutability,
independent instruction/list cloning, the raw Cecil instruction graph, locals,
and exception metadata. Real labels attach to the captured branch and switch
destinations; branch operands remain their original Cecil objects.

DrawModels must have no exception handlers. The adapter handles actual,
disjoint, contiguous try/finally regions by attaching begin-try, begin-finally
and end-exception markers at their real boundaries. Other EH shapes fail
closed. The current Paint snapshot has two such finally regions. Negative
controls reject missing/extra loads and altered loop semantics, and preserve
foreign fields with the same names.

The FieldInfo adapter is deliberately limited: only target field references
with validated owner, name, field type, owner/type scopes and resolved same-module
definition map to local fixture fields. Paint's other color reads also map to
that fixture field and must remain unchanged. Replacement MethodInfo values
refer to local fixture methods that throw if called. These substitutions
exercise the helpers' reflection API and equality behavior; they do not prove
game type compatibility, production replacement behavior, or runtime validity.

Cecil reads the DLL as data and refuses external assembly resolution. The
process observes assembly loads and rejects `Assembly-CSharp*`, `UnityEngine*`,
`BepInEx*`, and the `SphereOpt` plugin. No Harmony patch is applied. The label
generator defines labels without emitting instructions or compiling a
delegate. No game/plugin code, Unity rendering, or graphics performance is
tested. Keep reports and captured inputs under ignored `.local/`; ordinary
`bin/` and `obj/` build output is ignored.
