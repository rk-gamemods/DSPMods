using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SphereOpt.InstructionChecks;

internal sealed record DrawContract(int[] CursorIndices, int LayerLocal)
{
    internal static DrawContract Validate(Instruction[] il, VariableDefinition[] locals, FieldDefinition cursor)
    {
        var loads = il.Select((instruction, index) => (instruction, index))
            .Where(p => p.instruction.OpCode.Code == Code.Ldfld && Check.Matches(p.instruction.Operand, cursor))
            .Select(p => p.index).ToArray();
        Check.That(loads.Length == 2, "DrawModels must contain exactly two precise cursor loads");
        var first = loads[0];
        var second = loads[1];
        Check.That(first >= 12 && second >= first + 7 && second + 3 < il.Length, "Truncated shell loop");
        var layer = Local(il[first - 1], locals, store: false);
        Check.That(layer != null && layer.VariableType.FullName == "DysonSphereLayer" &&
            ReferenceEquals(layer, Local(il[second - 1], locals, store: false)), "Cursor loads require the same layer local");
        Check.That(!il.Skip(first).Take(second - first).Any(i => ReferenceEquals(Local(i, locals, store: true), layer)),
            "Layer local reassigned inside shell loop");
        var counter = Check.Field(cursor.Module, "DysonSphereSegmentRenderer", "_counter", "System.Int32");
        Check.That(il[first - 12].OpCode.Code == Code.Ldarg_0 &&
            Field(il[first - 11], counter) && Int(il[first - 10], 10) && il[first - 9].OpCode.Code == Code.Rem &&
            Branch(il[first - 8], Code.Brfalse, Code.Brfalse_S, il[first - 5]) && Int(il[first - 7], 0) &&
            Branch(il[first - 6], Code.Br, Code.Br_S, il[first + 2]), "Refresh remainder guard changed");
        Check.That(il[first - 5].OpCode.Code == Code.Ldarg_0 && Field(il[first - 4], counter) &&
            Int(il[first - 3], 10) && il[first - 2].OpCode.Code == Code.Div &&
            il[first + 1].OpCode.Code == Code.Rem && Local(il[first + 2], locals, store: true)?.VariableType.FullName == "System.Int32",
            "First cursor load must feed signed refresh remainder");
        var index = Local(il[first + 4], locals, store: true);
        Check.That(Int(il[first + 3], 1) && index?.VariableType.FullName == "System.Int32" &&
            Branch(il[first + 5], Code.Br, Code.Br_S, il[second - 2]) &&
            ReferenceEquals(Local(il[second - 2], locals, store: false), index), "Shell loop must initialize index to one and test before entry");
        Check.That(il[second + 1].OpCode.Code is Code.Blt or Code.Blt_S && il[second + 1].Operand is Instruction,
            "Second cursor must feed signed Blt");
        var body = Array.IndexOf(il, (Instruction)il[second + 1].Operand);
        var pool = Check.Field(cursor.Module, "DysonSphereLayer", "shellPool", "DysonShell[]");
        Check.That(body > first + 5 && body + 4 < second &&
            ReferenceEquals(Local(il[body], locals, store: false), layer) && Field(il[body + 1], pool) &&
            ReferenceEquals(Local(il[body + 2], locals, store: false), index) && il[body + 3].OpCode.Code == Code.Ldelem_Ref &&
            Local(il[body + 4], locals, store: true)?.VariableType.FullName == "DysonShell", "Blt must loop backward to this layer's indexed shellPool body");
        Check.That(ReferenceEquals(Local(il[second - 6], locals, store: false), index) && Int(il[second - 5], 1) &&
            il[second - 4].OpCode.Code == Code.Add && ReferenceEquals(Local(il[second - 3], locals, store: true), index),
            "Shell index must increment by one");
        var overlay = Check.Field(cursor.Module, "DysonSphereLayer", "paintingOverlayMat", "UnityEngine.Material");
        Check.That(ReferenceEquals(Local(il[second + 2], locals, store: false), layer) && Field(il[second + 3], overlay),
            "Blt false edge must directly enter the same layer's painting overlay");
        return new DrawContract(loads, layer.Index);
    }

    private static bool Field(Instruction instruction, FieldDefinition field) =>
        instruction.OpCode.Code == Code.Ldfld && Check.Matches(instruction.Operand, field);

    private static bool Branch(Instruction instruction, Code longCode, Code shortCode, Instruction target) =>
        (instruction.OpCode.Code == longCode || instruction.OpCode.Code == shortCode) && ReferenceEquals(instruction.Operand, target);

    private static bool Int(Instruction instruction, int value) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4_0 => value == 0, Code.Ldc_I4_1 => value == 1,
        Code.Ldc_I4_S => (sbyte)instruction.Operand == value,
        Code.Ldc_I4 => (int)instruction.Operand == value, _ => false
    };

    private static VariableDefinition Local(Instruction instruction, VariableDefinition[] locals, bool store)
    {
        var code = instruction.OpCode.Code;
        if ((!store && code is Code.Ldloc or Code.Ldloc_S) || (store && code is Code.Stloc or Code.Stloc_S))
            return instruction.Operand as VariableDefinition;
        var index = (store, code) switch
        {
            (false, Code.Ldloc_0) or (true, Code.Stloc_0) => 0,
            (false, Code.Ldloc_1) or (true, Code.Stloc_1) => 1,
            (false, Code.Ldloc_2) or (true, Code.Stloc_2) => 2,
            (false, Code.Ldloc_3) or (true, Code.Stloc_3) => 3, _ => -1
        };
        return index >= 0 && index < locals.Length ? locals[index] : null;
    }

    internal static Instruction[] CloneGraph(Instruction[] original)
    {
        var copy = original.Select(i => { var c = Instruction.Create(OpCodes.Nop); c.OpCode = i.OpCode; c.Operand = i.Operand; return c; }).ToArray();
        var map = original.Select((instruction, index) => (instruction, index)).ToDictionary(p => p.instruction, p => copy[p.index]);
        foreach (var instruction in copy)
        {
            if (instruction.Operand is Instruction target) instruction.Operand = map[target];
            else if (instruction.Operand is Instruction[] targets) instruction.Operand = targets.Select(t => map[t]).ToArray();
        }
        return copy;
    }
}
