using System.Reflection;
using System.Text;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SphereOpt;
using E = System.Reflection.Emit.OpCodes;

namespace SphereOpt.InstructionChecks;

internal static class StackChecks
{
    internal const string InstalledSha256 = "721ad22d207eea7ed8a33fe0a5d57682fa715517db5e858a529e4178967f33ba";

    internal static object Run(ModuleDefinition module, CapturedMethod paint, int store, ProofMetadata installed)
    {
        var color = typeof(ShellFixture).GetField(nameof(ShellFixture.color));
        var setter = typeof(FixtureHooks).GetMethod(nameof(FixtureHooks.Color));
        var update = typeof(ShellFixture).GetMethod(nameof(ShellFixture.SetMaterialDynamicVars));
        var receiver = paint.Input[store - 12];
        Check.That(receiver.opcode == E.Ldloc_S && receiver.operand is VariableDefinition local &&
            local.VariableType.FullName == "DysonShell", "DSP paint insertion's relative receiver is no longer a DysonShell local");
        var candidate = ColorStoreRewriter.Replace(paint.Input, color, setter).ToArray();
        paint.Verify(candidate, [store], setter);
        var dsp = Apply(paint.Input, input => DspTransformations.UpdateShellColourPatch2(input, null));
        Check.That(dsp.Length == paint.Input.Count + 2, "DSP paint must insert exactly two instructions");
        CapturedMethod.VerifyClone(paint.Input, dsp.Where((_, i) => i != store + 1 && i != store + 2).ToArray(), [], null);
        Check.That(dsp[store + 1].opcode == receiver.opcode && ReferenceEquals(dsp[store + 1].operand, receiver.operand) &&
            dsp[store + 2].opcode == E.Call && Equals(dsp[store + 2].operand, update) &&
            dsp.Skip(store + 1).Take(2).All(i => i.labels.Count == 0 && i.blocks.Count == 0),
            "DSP paint inserted the wrong receiver/call or unexpected entry metadata");
        var reverse = ColorStoreRewriter.Replace(dsp, color, setter).ToArray();
        CapturedMethod.VerifyClone(dsp, reverse, [store], setter);
        Check.That(Calls(reverse, setter) == 1 && Calls(reverse, update) == 1 &&
            reverse[store].opcode == E.Call && Equals(reverse[store].operand, setter) &&
            reverse[store + 1].opcode == receiver.opcode && ReferenceEquals(reverse[store + 1].operand, receiver.operand) &&
            reverse[store + 2].opcode == E.Call && Equals(reverse[store + 2].operand, update),
            "DSP-first paint lost setter / shell receiver / material update adjacency");

        // Real pinned CodeMatcher silently relocates a failed search after Advance.
        var badOrder = Apply(candidate, input => DspTransformations.UpdateShellColourPatch2(input, null));
        var probe = new CodeMatcher(candidate, null).MatchForward(true, new CodeMatch(E.Stfld, color));
        var failedMatch = probe.Pos;
        Check.That(probe.IsInvalid && failedMatch == candidate.Length, "Candidate-first paint unexpectedly retains a target store");
        probe.Advance(-12);
        Check.That(probe.Pos == candidate.Length - 12 && probe.Opcode == E.Ldarg_0 && probe.Operand == null,
            "Candidate-first missing-match relocation changed");
        probe.Advance(13);
        Check.That(probe.Pos == candidate.Length && candidate[^1].opcode == E.Ret &&
            badOrder.Length == candidate.Length + 2 && badOrder[^3].opcode == E.Ret &&
            badOrder[^2].opcode == E.Ldarg_0 && badOrder[^2].operand == null &&
            badOrder[^1].opcode == E.Call && Equals(badOrder[^1].operand, update) &&
            badOrder.TakeLast(2).All(i => i.labels.Count == 0 && i.blocks.Count == 0),
            "Candidate-first negative control no longer appends the misplaced call after ret");
        CapturedMethod.VerifyClone(candidate, badOrder.Take(candidate.Length).ToArray(), [], null);
        Check.That(Calls(badOrder, setter) == 1 && Calls(badOrder, update) == 1 &&
            !Equals(badOrder[store + 2].operand, update), "Bad order was not distinguished from the positive composition");
        paint.AssertInputUnchanged();

        return new
        {
            paint = new
            {
                status = "passed", candidateAlone = true, dspoAlone = true, dspoThenCandidate = true,
                storeIndex = store, receiverLocal = ((VariableDefinition)receiver.operand).Index,
                setterIndex = store, materialUpdateIndex = store + 2, composedInstructions = reverse.Length,
                candidateThenDspo = new { status = "demonstrated-misplaced-insertion", throws = false,
                    failedMatchPosition = failedMatch, insertionIndex = candidate.Length,
                    copiedReceiver = "ldarg.0 (brush this)", appendedAfterFinalRet = true },
                everyOtherInstructionOperandLabelAndEhBlockPreserved = true, capturedInputUnchanged = true
            },
            drawModels = Draw(module), installedSourceEquivalence = Installed(installed),
            sourceRevision = "d46a01cb5db971fc7935e0fb13f172d367b96638",
            sourceFileSha256 = new { shellShaderVarOpt = "0b5876b14626abe5e8b0d7c3cd5824a477453f4237058e95e4bf0e996fcd957e",
                sphereBufferOpt = "66b2c6f3fbf3f58f3dc1df4956087e4eb61d944e46da64f1f6687739dfa523ba" }
        };
    }

    private static object Draw(ModuleDefinition module)
    {
        var method = Check.Method(module, "DysonSphereSegmentRenderer", "DrawModels", "ERenderPlace", "System.Int32", "System.Int32");
        var cursor = Check.Field(module, "DysonSphereLayer", "shellCursor", "System.Int32");
        var dynamic = Check.Method(module, "DysonShell", "SetMaterialDynamicVars");
        var sync = module.Types.Single(t => t.FullName == "DysonSphereSegmentRenderer").NestedTypes.Single(t => t.Name == "Batch")
            .Methods.Single(m => m.Name == "SyncBufferData");
        var cursorField = typeof(LayerFixture).GetField(nameof(LayerFixture.shellCursor));
        var getter = typeof(FixtureHooks).GetMethod(nameof(FixtureHooks.Cursor));
        var dynamicFixture = typeof(ShellFixture).GetMethod(nameof(ShellFixture.SetMaterialDynamicVars));
        var syncFixture = typeof(SegmentRendererFixture.Batch).GetMethod(nameof(SegmentRendererFixture.Batch.SyncBufferData));
        var dirty = typeof(BufferHooks).GetMethod(nameof(BufferHooks.SyncDirty));
        var capture = new CapturedMethod(method, cursor, cursorField, (dynamic, dynamicFixture), (sync, syncFixture));
        var semantic = DrawContract.Validate(method.Body.Instructions.ToArray(), method.Body.Variables.ToArray(), cursor);
        Check.That(method.Body.ExceptionHandlers.Count == 0, "DrawModels composition requires no EH regions");
        var shaderCall = OneCall(capture.Input, dynamicFixture);
        var syncCall = OneCall(capture.Input, syncFixture);
        Check.That(shaderCall > 0 && capture.Input[shaderCall - 1].opcode == E.Ldloc_S &&
            capture.Input[shaderCall - 1].operand is VariableDefinition shell && shell.VariableType.FullName == "DysonShell",
            "Removed shader call must consume a captured DysonShell local");
        Check.That(syncCall >= 4 && capture.Input[syncCall - 4].opcode == E.Ldarg_0 &&
            capture.Input[syncCall - 3].opcode == E.Ldfld && capture.Input[syncCall - 3].operand is FieldReference batches &&
            batches.FullName == "DysonSphereSegmentRenderer/Batch[] DysonSphereSegmentRenderer::batches" &&
            capture.Input[syncCall - 2].opcode == E.Ldloc_S && capture.Input[syncCall - 2].operand is VariableDefinition batchIndex &&
            batchIndex.VariableType.FullName == "System.Int32" && capture.Input[syncCall - 1].opcode == E.Ldelem_Ref,
            "Sync replacement must preserve renderer / batch index and remove only array lookup / sync call");
        Check.That(capture.Input.Skip(syncCall - 1).Take(2).All(i => i.labels.Count == 0 && i.blocks.Count == 0),
            "Sync removal would discard captured entry labels or EH boundaries");
        Check.That(dirty.IsStatic && dirty.ReturnType == typeof(void) && dirty.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual(new[] { typeof(SegmentRendererFixture), typeof(int), typeof(int), typeof(int) }),
            "Delegate-call adapter must consume renderer, batch index, editor mask, game mask");

        var orders = new[] { "SBC", "SCB", "BSC", "BCS", "CSB", "CBS" };
        foreach (var order in orders)
        {
            CodeInstruction[] output = CapturedMethod.Clone(capture.Input).ToArray();
            foreach (var transform in order)
                output = Apply(output, input => transform switch
                {
                    'S' => DspTransformations.RemoveShaderVarCall(input, null),
                    'B' => DspTransformations.DrawModels_SyncBuffers_Patch(input, null),
                    'C' => ShellCursorRewriter.Replace(input, cursorField, getter),
                    _ => throw new InvalidOperationException("Unknown transform")
                });

            // Independent expected edit contract, preserving all untouched original
            // instructions, operands, labels and block objects, including node/frame
            // draw calls and overlay code. Nop retains the source matcher's operand.
            var expected = CapturedMethod.Clone(capture.Input);
            expected[shaderCall - 1].opcode = E.Nop;
            expected[shaderCall].opcode = E.Nop;
            expected[syncCall - 3].opcode = E.Nop;
            foreach (var index in semantic.CursorIndices)
            {
                expected[index].opcode = E.Call;
                expected[index].operand = getter;
            }
            expected.RemoveRange(syncCall - 1, 2);
            expected.InsertRange(syncCall - 1, new[] { new CodeInstruction(E.Ldarg_2), new CodeInstruction(E.Ldarg_3), new CodeInstruction(E.Call, dirty) });
            CapturedMethod.VerifyClone(expected, output, [], null);
            Check.That(output.Length == capture.Input.Count + 1 && Calls(output, getter) == 2 &&
                Calls(output, dirty) == 1 && Calls(output, dynamicFixture) == 0 && Calls(output, syncFixture) == 0,
                "DrawModels composition did not retain exactly two cursor substitutions and one dirty-sync call");
        }
        // Exact metadata mapping rejects same-name methods in foreign owners/scopes
        // before attempting resolution. These must remain original Cecil objects.
        foreach (var target in new[] { dynamic, sync })
        {
            foreach (var owner in new[] { new TypeReference("", "ForeignOwner", module, module),
                new TypeReference(target.DeclaringType.Namespace, target.DeclaringType.Name, module,
                    new AssemblyNameReference("ForeignMethods", new Version(1, 0))) })
            {
                var foreign = new MethodReference(target.Name, target.ReturnType, owner) { HasThis = true };
                Check.That(ReferenceEquals(capture.MapOperand(foreign), foreign), "Method adapter matched a foreign method");
            }
            var wrongReturn = new MethodReference(target.Name, module.TypeSystem.String, target.DeclaringType) { HasThis = true };
            var wrongParameters = new MethodReference(target.Name, target.ReturnType, target.DeclaringType) { HasThis = true };
            wrongParameters.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            var wrongStatic = new MethodReference(target.Name, target.ReturnType, target.DeclaringType) { HasThis = false };
            foreach (var inexact in new[] { wrongReturn, wrongParameters, wrongStatic })
                Check.That(ReferenceEquals(capture.MapOperand(inexact), inexact), "Method adapter matched an inexact signature");
        }
        capture.AssertInputUnchanged();
        return new { status = "passed", permutationsPassed = orders.Length, orders,
            orderKey = "S=ShellShaderVarOpt, B=SphereBufferOpt, C=real ShellCursorRewriter",
            captured = capture.Report(semantic.CursorIndices), shaderCallIndex = shaderCall, syncCallIndex = syncCall,
            cursorSubstitutions = 2, allOtherInstructionsOperandsLabelsAndEhPreserved = true,
            methodAdapterNegativeControls = new { foreignOwnerScope = 4, wrongReturnParametersOrStatic = 6 },
            delegateAdapter = "Static throwing fixture call; same four argument stack arity. No EmitDelegate or dirty-mask callback execution." };
    }

    private static CodeInstruction[] Apply(IReadOnlyList<CodeInstruction> input,
        Func<IEnumerable<CodeInstruction>, IEnumerable<CodeInstruction>> transform)
    {
        var snapshots = input.Select(CapturedMethod.Snapshot.Take).ToArray();
        var output = transform(input).ToArray();
        CapturedMethod.VerifyClone(input, CapturedMethod.Clone(input).ToArray(), [], null, snapshots);
        Check.That(output.All(i => !input.Any(old => ReferenceEquals(old, i) || ReferenceEquals(old.labels, i.labels) ||
            ReferenceEquals(old.blocks, i.blocks))), "Transformation shared mutable instruction/list objects with its input");
        return output;
    }

    private static int Calls(IEnumerable<CodeInstruction> input, MethodInfo method) =>
        input.Count(i => (i.opcode == E.Call || i.opcode == E.Callvirt) && Equals(i.operand, method));

    private static int OneCall(IReadOnlyList<CodeInstruction> input, MethodInfo method)
    {
        var matches = Enumerable.Range(0, input.Count).Where(i => input[i].opcode == E.Callvirt && Equals(input[i].operand, method)).ToArray();
        Check.That(matches.Length == 1, "Expected exactly one captured call to " + method.Name);
        return matches[0];
    }

    private static object Installed(ProofMetadata installed)
    {
        if (installed == null) return new { status = "not-requested", reason = "Supply --dsp-optimizations for installed/source checks" };
        Check.That(ProofMetadata.Hash(installed.Bytes) == InstalledSha256 && installed.Assembly.Name.Name == "DSPOptimizations",
            "--dsp-optimizations must be the reviewed DSPOptimizations 1.1.19 DLL (SHA-256 mismatch)");
        using var self = new ProofMetadata(Assembly.GetExecutingAssembly().Location);
        var results = new List<object>();
        foreach (var name in new[] { "UpdateShellColourPatch2", "RemoveShaderVarCall" })
        {
            var actual = ProofMetadata.Method(installed.Assembly, PatchOrderChecks.PaintPatchType, name);
            var adapted = ProofMetadata.Method(self.Assembly, typeof(DspTransformations).FullName, name);
            Check.That(actual.IsStatic && adapted.IsStatic && actual.ReturnType.FullName == adapted.ReturnType.FullName &&
                actual.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(adapted.Parameters.Select(p => p.ParameterType.FullName)) &&
                actual.Body.Variables.Select(v => v.VariableType.FullName).SequenceEqual(adapted.Body.Variables.Select(v => v.VariableType.FullName)) &&
                actual.Body.ExceptionHandlers.Count == 0 && adapted.Body.ExceptionHandlers.Count == 0,
                "Installed/adapted signature, locals or EH differ: " + name);
            var left = ProofMetadata.Normalize(actual);
            var right = ProofMetadata.Normalize(adapted);
            var mismatch = Enumerable.Range(0, Math.Min(left.Length, right.Length)).FirstOrDefault(i => left[i] != right[i], -1);
            Check.That(left.SequenceEqual(right), $"Installed/adapted normalized body differs: {name}, counts {left.Length}/{right.Length}, index {mismatch}: " +
                (mismatch < 0 ? "length" : left[mismatch] + " versus " + right[mismatch]));
            results.Add(new { method = name, token = $"0x{actual.MetadataToken.ToInt32():x8}",
                installedInstructions = actual.Body.Instructions.Count, adaptedInstructions = adapted.Body.Instructions.Count,
                normalizedInstructions = left.Length, normalizedSha256 = ProofMetadata.Hash(Encoding.UTF8.GetBytes(string.Join("\n", left) + "\n")),
                signatureLocalsEhAndNormalizedBodyEquivalent = true });
        }
        var sync = ProofMetadata.Method(installed.Assembly, "DSPOptimizations.SphereBufferOpt/Patch", "DrawModels_SyncBuffers_Patch");
        var adaptedSync = ProofMetadata.Method(self.Assembly, typeof(DspTransformations).FullName, sync.Name);
        var syncBody = ProofMetadata.Normalize(sync);
        var adaptedSyncBody = ProofMetadata.Normalize(adaptedSync);
        var prefix = SyncPrefix(syncBody);
        var adaptedPrefix = SyncPrefix(adaptedSyncBody);
        var suffix = SyncSuffix(syncBody);
        var adaptedSuffix = SyncSuffix(adaptedSyncBody);
        Check.That(prefix.SequenceEqual(adaptedPrefix) && suffix.SequenceEqual(adaptedSuffix),
            "Installed/adapted SphereBufferOpt matcher/removal/mask-load prefix or return suffix differs");
        var callbacks = sync.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldftn).Select(i => (MethodReference)i.Operand).ToArray();
        Check.That(callbacks.Length == 1 && ReferenceEquals(callbacks[0].DeclaringType.Scope, installed.Assembly.MainModule),
            "Expected one same-module SphereBufferOpt callback");
        var callback = callbacks[0].Resolve();
        Check.That(callback.ReturnType.FullName == "System.Void" && callback.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "DysonSphereSegmentRenderer", "System.Int32", "System.Int32", "System.Int32" }) &&
            sync.Body.Instructions.Any(i => i.Operand is GenericInstanceMethod factory && factory.Name == "EmitDelegate" &&
                factory.DeclaringType.FullName == "HarmonyLib.Transpilers" && factory.GenericArguments.Count == 1 &&
                factory.GenericArguments[0].FullName == "System.Action`4<DysonSphereSegmentRenderer,System.Int32,System.Int32,System.Int32>"),
            "Installed SphereBufferOpt delegate signature differs from the four-argument call adapter");
        var fields = callback.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().ToArray();
        Check.That(fields.Any(f => f.DeclaringType.FullName == "DysonSphereSegmentRenderer" && f.Name == "layersDirtyMask" &&
            f.FieldType.FullName == "System.Int32[]") && fields.Any(f => f.Name == "batches") &&
            fields.Any(f => f.DeclaringType.FullName == "DysonSphere" && f.Name == "renderPlace") &&
            callback.Body.Instructions.Any(i => i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "DysonSphereSegmentRenderer/Batch" && m.Name == "SyncBufferData"),
            "Installed dirty-mask callback metadata no longer matches the reviewed adapter boundary");
        return new { status = "passed", input = installed.Identity(), equivalentMethods = results,
            syncScaffold = new { matchingRemovalAndFirstTwoInsertedMaskLoadsEquivalent = true,
                normalizedPrefixInstructions = prefix.Length, normalizedReturnSuffixInstructions = suffix.Length,
                callbackAndEmitDelegateExcluded = true },
            syncCallback = new { method = callback.FullName, token = $"0x{callback.MetadataToken.ToInt32():x8}",
                injectedDirtyFieldReferenceObserved = true, originalCapturedGameFieldRequired = false,
                limits = "Metadata references only; callback body equivalence, preloader field injection, delegate emission, dirty-mask behavior untested" },
            normalization = "Ignore nops, short opcode forms, branch offsets and type/assembly scopes; canonicalize explicit fixture type names only" };
    }

    private static string[] SyncPrefix(string[] body)
    {
        var remove = Array.FindIndex(body, line => line.Contains("HarmonyLib.CodeMatcher::RemoveInstructions(System.Int32)", StringComparison.Ordinal));
        Check.That(remove >= 0, "SphereBufferOpt prefix no longer contains RemoveInstructions");
        var stores = Enumerable.Range(remove + 1, body.Length - remove - 1).Where(i => body[i] == "stelem.ref").Take(2).ToArray();
        Check.That(stores.Length == 2, "SphereBufferOpt mask insertion prefix is truncated");
        return body.Take(stores[1] + 1).ToArray();
    }

    private static string[] SyncSuffix(string[] body)
    {
        var insert = Array.FindIndex(body, line => line.Contains("HarmonyLib.CodeMatcher::InsertAndAdvance(HarmonyLib.CodeInstruction[])", StringComparison.Ordinal));
        Check.That(insert >= 0, "SphereBufferOpt return suffix no longer contains InsertAndAdvance");
        return body.Skip(insert).ToArray();
    }
}
