using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EmitCode = System.Reflection.Emit.OpCode;
using EmitCodes = System.Reflection.Emit.OpCodes;
using CecilInstruction = Mono.Cecil.Cil.Instruction;

namespace SphereOpt.InstructionChecks;

internal sealed class CapturedMethod
{
    private static readonly Dictionary<ushort, EmitCode> Opcodes = typeof(EmitCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(EmitCode))
        .Select(f => (EmitCode)f.GetValue(null)).ToDictionary(op => unchecked((ushort)op.Value));
    private readonly MethodDefinition method;
    private readonly FieldDefinition target;
    private readonly FieldInfo fixture;
    private readonly (MethodDefinition Target, MethodInfo Fixture)[] methodFixtures;
    private readonly CecilInstruction[] raw;
    private readonly Func<bool>[] rawChecks;
    private readonly Snapshot[] before;
    private readonly int labelCount;
    internal List<CodeInstruction> Input { get; }

    internal CapturedMethod(MethodDefinition method, FieldDefinition target, FieldInfo fixture,
        params (MethodDefinition Target, MethodInfo Fixture)[] methodFixtures)
    {
        this.method = method;
        this.target = target;
        this.fixture = fixture;
        this.methodFixtures = methodFixtures;
        Check.That(methodFixtures.All(pair => !pair.Target.IsStatic && !pair.Fixture.IsStatic &&
            pair.Target.ReturnType.FullName == "System.Void" && pair.Fixture.ReturnType == typeof(void) &&
            pair.Target.Parameters.Count == 0 && pair.Fixture.GetParameters().Length == 0),
            "Only validated parameterless instance-void method adapters are supported");
        var body = method.Body;
        raw = body.Instructions.ToArray();
        var rawState = raw.Select(i => (i.OpCode, i.Operand, i.Offset, i.Previous, i.Next,
            Targets: (i.Operand as CecilInstruction[])?.ToArray())).ToArray();
        var locals = body.Variables.Select(v => (Variable: v, v.VariableType, v.Index, v.IsPinned)).ToArray();
        var regions = body.ExceptionHandlers.Select(h => (Handler: h, h.HandlerType, h.TryStart, h.TryEnd,
            h.HandlerStart, h.HandlerEnd, h.FilterStart, h.CatchType)).ToArray();
        var initLocals = body.InitLocals;
        var maxStack = body.MaxStackSize;
        var codeSize = body.CodeSize;
        rawChecks =
        [
            () => body.Instructions.Count == raw.Length && raw.Select((instruction, i) =>
                ReferenceEquals(body.Instructions[i], instruction) && instruction.OpCode == rawState[i].OpCode &&
                ReferenceEquals(instruction.Operand, rawState[i].Operand) && instruction.Offset == rawState[i].Offset &&
                ReferenceEquals(instruction.Previous, rawState[i].Previous) && ReferenceEquals(instruction.Next, rawState[i].Next) &&
                (rawState[i].Targets == null || ((CecilInstruction[])instruction.Operand).SequenceEqual(rawState[i].Targets))).All(v => v),
            () => body.Variables.Count == locals.Length && locals.Select((v, i) =>
                ReferenceEquals(body.Variables[i], v.Variable) && ReferenceEquals(v.Variable.VariableType, v.VariableType) &&
                v.Variable.Index == v.Index && v.Variable.IsPinned == v.IsPinned).All(v => v),
            () => body.ExceptionHandlers.Count == regions.Length && regions.Select((h, i) =>
                ReferenceEquals(body.ExceptionHandlers[i], h.Handler) && h.Handler.HandlerType == h.HandlerType &&
                ReferenceEquals(h.Handler.TryStart, h.TryStart) && ReferenceEquals(h.Handler.TryEnd, h.TryEnd) &&
                ReferenceEquals(h.Handler.HandlerStart, h.HandlerStart) && ReferenceEquals(h.Handler.HandlerEnd, h.HandlerEnd) &&
                ReferenceEquals(h.Handler.FilterStart, h.FilterStart) && ReferenceEquals(h.Handler.CatchType, h.CatchType)).All(v => v),
            () => body.InitLocals == initLocals && body.MaxStackSize == maxStack && body.CodeSize == codeSize
        ];
        Input = raw.Select(i => new CodeInstruction(Opcodes[unchecked((ushort)i.OpCode.Value)], MapOperand(i.Operand))).ToList();
        // Define labels only. Never emit IL, compile a delegate, or execute this DynamicMethod.
        var generator = new DynamicMethod("CapturedBranchLabelsOnly", typeof(void), Type.EmptyTypes).GetILGenerator();
        var destinations = raw.SelectMany(i => i.Operand switch
        {
            CecilInstruction destination => new[] { destination },
            CecilInstruction[] destinations => destinations, _ => Array.Empty<CecilInstruction>()
        }).Distinct().ToArray();
        foreach (var destination in destinations)
        {
            var index = Array.IndexOf(raw, destination);
            Check.That(index >= 0, "Branch points outside the captured method");
            Input[index].labels.Add(generator.DefineLabel());
        }
        labelCount = destinations.Length;
        Check.That(labelCount > 0, "Actual methods must exercise nonempty branch labels");
        MapFinallyRegions();
        before = Input.Select(Snapshot.Take).ToArray();
        for (var index = 0; index < raw.Length; index++)
            Check.That(Input[index].opcode.Value == raw[index].OpCode.Value &&
                ReferenceEquals(Input[index].operand, MapOperand(raw[index].Operand)),
                "Cecil adapter changed an opcode or a non-target operand identity");
    }

    internal object MapOperand(object operand)
    {
        if (Check.Matches(operand, target)) return fixture;
        foreach (var pair in methodFixtures)
            if (Check.Matches(operand, pair.Target)) return pair.Fixture;
        return operand;
    }

    private void MapFinallyRegions()
    {
        var end = -1;
        foreach (var region in method.Body.ExceptionHandlers.OrderBy(h => Array.IndexOf(raw, h.TryStart)))
        {
            var start = Array.IndexOf(raw, region.TryStart);
            var handler = Array.IndexOf(raw, region.HandlerStart);
            var after = Array.IndexOf(raw, region.HandlerEnd);
            Check.That(region.HandlerType == ExceptionHandlerType.Finally && region.FilterStart == null && region.CatchType == null &&
                start >= end && start >= 0 && handler > start && after > handler &&
                ReferenceEquals(region.TryEnd, region.HandlerStart) && raw[after - 1].OpCode.Code == Code.Endfinally,
                "Unsupported captured EH shape: only disjoint, contiguous try/finally with explicit endfinally is supported");
            Input[start].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            Input[handler].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
            Input[after - 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            end = after;
        }
        Check.That(Input.Sum(i => i.blocks.Count) == method.Body.ExceptionHandlers.Count * 3,
            "Every captured finally must contribute exactly its three real boundary markers");
    }

    internal int Verify(IEnumerable<CodeInstruction> transformed, int[] indices, MethodInfo replacement)
    {
        var output = transformed.ToArray();
        VerifyClone(Input, output, indices, replacement, before);
        AssertRawUnchanged();
        return indices.Length;
    }

    internal static List<CodeInstruction> Clone(IEnumerable<CodeInstruction> input) => input.Select(i => new CodeInstruction(i)).ToList();

    internal static void VerifyClone(IReadOnlyList<CodeInstruction> input, CodeInstruction[] output,
        int[] indices, MethodInfo replacement, Snapshot[] snapshots = null)
    {
        snapshots ??= input.Select(Snapshot.Take).ToArray();
        var targets = indices.ToHashSet();
        Check.That(targets.Count == indices.Length && targets.All(i => i >= 0 && i < input.Count), "Invalid substitution indices");
        Check.That(input.Count == snapshots.Length && output.Length == input.Count, "Instruction count changed");
        for (var index = 0; index < input.Count; index++)
        {
            var original = snapshots[index];
            Check.That(ReferenceEquals(input[index], original.Instruction) && input[index].opcode == original.Opcode &&
                ReferenceEquals(input[index].operand, original.Operand) && ReferenceEquals(input[index].labels, original.LabelList) &&
                ReferenceEquals(input[index].blocks, original.BlockList) && input[index].labels.SequenceEqual(original.Labels) &&
                SameBlocks(input[index].blocks, original.Blocks), $"Input instruction or metadata mutated at index {index}");
            Check.That(!ReferenceEquals(input[index], output[index]) && !ReferenceEquals(input[index].labels, output[index].labels) &&
                !ReferenceEquals(input[index].blocks, output[index].blocks), $"Instruction metadata not independently cloned at index {index}");
            var normalizedOpcode = output[index].opcode;
            var normalizedOperand = output[index].operand;
            if (targets.Contains(index))
            {
                Check.That(output[index].opcode == EmitCodes.Call && ReferenceEquals(output[index].operand, replacement),
                    $"Intended substitution missing at index {index}");
                normalizedOpcode = original.Opcode;
                normalizedOperand = original.Operand;
            }
            Check.That(normalizedOpcode == original.Opcode && ReferenceEquals(normalizedOperand, original.Operand) &&
                output[index].labels.SequenceEqual(original.Labels) && SameBlocks(output[index].blocks, original.Blocks),
                $"Non-substitution opcode, operand identity, labels or blocks changed at index {index}");
        }
    }

    private static bool SameBlocks(IReadOnlyList<ExceptionBlock> actual, BlockSnapshot[] expected) =>
        actual.Count == expected.Length && actual.Select((b, i) => ReferenceEquals(b, expected[i].Block) &&
            b.blockType == expected[i].BlockType && ReferenceEquals(b.catchType, expected[i].CatchType)).All(v => v);

    internal void AssertRawUnchanged() => Check.That(rawChecks.All(check => check()), "Raw captured body, locals or EH metadata changed");

    internal void AssertInputUnchanged()
    {
        VerifyClone(Input, Clone(Input).ToArray(), Array.Empty<int>(), null, before);
        AssertRawUnchanged();
    }

    internal object Report(int[] indices) => new
    {
        method = method.DeclaringType.FullName + "." + method.Name,
        methodToken = $"0x{method.MetadataToken.ToInt32():x8}", instructions = raw.Length,
        locals = method.Body.Variables.Count, exceptionRegions = method.Body.ExceptionHandlers.Count,
        exceptionBoundaryBlocks = Input.Sum(i => i.blocks.Count), realBranchLabels = labelCount,
        targetFieldReferencesAdapted = raw.Count(i => Check.Matches(i.Operand, target)),
        targetMethodReferencesAdapted = raw.Count(i => methodFixtures.Any(pair => Check.Matches(i.Operand, pair.Target))),
        substitutionOffsets = indices.Select(i => $"0x{raw[i].Offset:x4}").ToArray(),
        allOtherInstructionsAndMetadataPreserved = true, rawBodyAndLocalsUnchanged = true
    };

    internal sealed record BlockSnapshot(ExceptionBlock Block, ExceptionBlockType BlockType, Type CatchType);

    internal sealed record Snapshot(CodeInstruction Instruction, EmitCode Opcode, object Operand,
        List<Label> LabelList, Label[] Labels, List<ExceptionBlock> BlockList, BlockSnapshot[] Blocks)
    {
        internal static Snapshot Take(CodeInstruction i) => new(i, i.opcode, i.operand, i.labels, i.labels.ToArray(),
            i.blocks, i.blocks.Select(b => new BlockSnapshot(b, b.blockType, b.catchType)).ToArray());
    }
}
