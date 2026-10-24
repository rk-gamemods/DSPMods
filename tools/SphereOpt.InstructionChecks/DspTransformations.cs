// Adapted from Selsion/DSPMods, DSPOptimizations 1.1.19, revision
// d46a01cb5db971fc7935e0fb13f172d367b96638, Optimizations/ShellShaderVarOpt.cs
// and Optimizations/SphereBufferOpt.cs. Copyright (c) 2022 Selsion, MIT.
// See THIRD_PARTY_NOTICES.md for the full notice and adaptation boundaries.
using HarmonyLib;
using System.Reflection.Emit;
using DysonShell = global::ShellFixture;
using DysonSphereSegmentRenderer = SphereOpt.InstructionChecks.SegmentRendererFixture;

namespace SphereOpt.InstructionChecks;

internal static class DspTransformations
{
    // Method body retained; local aliases substitute metadata-only fixture types.
    internal static IEnumerable<CodeInstruction> UpdateShellColourPatch2(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        CodeMatcher matcher = new CodeMatcher(instructions, generator);

        // add a call to SetMaterialDynamicVars() after the colour is updated
        matcher.MatchForward(true,
            new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(DysonShell), nameof(DysonShell.color)))
        ).Advance(-12);

        var shellVarOpCode = matcher.Opcode;
        var shellVarOperand = matcher.Operand;

        matcher.Advance(13).InsertAndAdvance(
            new CodeInstruction(shellVarOpCode, shellVarOperand),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DysonShell), nameof(DysonShell.SetMaterialDynamicVars)))
        );

        return matcher.InstructionEnumeration();
    }

    internal static IEnumerable<CodeInstruction> RemoveShaderVarCall(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        CodeMatcher matcher = new CodeMatcher(instructions, generator);

        matcher.MatchForward(false,
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DysonShell), nameof(DysonShell.SetMaterialDynamicVars)))
        ).Advance(-1)
        .SetOpcodeAndAdvance(OpCodes.Nop)
        .SetOpcodeAndAdvance(OpCodes.Nop);

        return matcher.InstructionEnumeration();
    }

    internal static IEnumerable<CodeInstruction> DrawModels_SyncBuffers_Patch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        CodeMatcher matcher = new CodeMatcher(instructions, generator);

        matcher.MatchForward(false,
            new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(DysonSphereSegmentRenderer.Batch), nameof(DysonSphereSegmentRenderer.Batch.SyncBufferData)))
        ).Advance(-3).SetOpcodeAndAdvance(OpCodes.Nop)
        .Advance(1).RemoveInstructions(2)
        .InsertAndAdvance(
            // already have arg0, the idx
            new CodeInstruction(OpCodes.Ldarg_2), // editorMask
            new CodeInstruction(OpCodes.Ldarg_3), // gameMask
            // Adapter for the source's EmitDelegate<Action<renderer,int,int,int>>.
            // No delegate factory, callback, injected field, or game code runs.
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BufferHooks), nameof(BufferHooks.SyncDirty)))
        );

        return matcher.InstructionEnumeration();
    }
}

internal sealed class SegmentRendererFixture
{
    internal sealed class Batch
    {
        public void SyncBufferData() => throw new NotSupportedException("Metadata-only fixture");
    }
}

internal static class BufferHooks
{
    public static void SyncDirty(SegmentRendererFixture renderer, int batchIndex, int editorMask, int gameMask) =>
        throw new NotSupportedException("Metadata-only delegate-call adapter");
}
