using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using Mono.Cecil;
using SphereOpt;
using SphereOpt.InstructionChecks;
using CecilCode = Mono.Cecil.Cil.Code;

var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
void Record(Assembly assembly) { lock (loaded) loaded.Add(assembly.GetName().Name ?? ""); }
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Record(assembly);
AppDomain.CurrentDomain.AssemblyLoad += (_, eventArgs) => Record(eventArgs.LoadedAssembly);
try
{
    Check.That(args.Length is 2 or 4 && args[0] == "--assembly" &&
        (args.Length == 2 || args[2] == "--report"),
        "Usage: --assembly <captured Assembly-CSharp.dll> [--report <local JSON path>]");
    AssertIsolation();
    var bytes = File.ReadAllBytes(args[1]);
    using var stream = new MemoryStream(bytes, writable: false);
    using var assembly = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
    {
        AssemblyResolver = new RejectResolution(), ReadSymbols = false,
        ReadingMode = ReadingMode.Deferred
    });
    var module = assembly.MainModule;
    Check.That(assembly.Name.Name == "Assembly-CSharp", "Input must be the captured Assembly-CSharp assembly");
    var draw = Check.Method(module, "DysonSphereSegmentRenderer", "DrawModels",
        "ERenderPlace", "System.Int32", "System.Int32");
    var paint = Check.Method(module, "UIDysonBrush_Paint", "_OnUpdate");
    var cursor = Check.Field(module, "DysonSphereLayer", "shellCursor", "System.Int32");
    var color = Check.Field(module, "DysonShell", "color", "UnityEngine.Color32");
    var cursorFixture = typeof(LayerFixture).GetField(nameof(LayerFixture.shellCursor));
    var colorFixture = typeof(ShellFixture).GetField(nameof(ShellFixture.color));
    var getter = typeof(FixtureHooks).GetMethod(nameof(FixtureHooks.Cursor));
    var setter = typeof(FixtureHooks).GetMethod(nameof(FixtureHooks.Color));
    Check.That(getter.IsStatic && getter.ReturnType == cursorFixture.FieldType &&
        getter.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { cursorFixture.DeclaringType }),
        "Fixture getter must consume one receiver and return the fixture field type");
    Check.That(setter.IsStatic && setter.ReturnType == typeof(void) &&
        setter.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { colorFixture.DeclaringType, colorFixture.FieldType }),
        "Fixture setter must consume exactly the fixture receiver and field value");

    var drawInput = new CapturedMethod(draw, cursor, cursorFixture);
    var paintInput = new CapturedMethod(paint, color, colorFixture);
    var semantic = DrawContract.Validate(draw.Body.Instructions.ToArray(), draw.Body.Variables.ToArray(), cursor);
    Check.That(draw.Body.ExceptionHandlers.Count == 0, "DrawModels currently requires zero exception regions");
    var stores = paint.Body.Instructions.Select((instruction, index) => (instruction, index))
        .Where(p => p.instruction.OpCode.Code == CecilCode.Stfld && Check.Matches(p.instruction.Operand, color))
        .Select(p => p.index).ToArray();
    Check.That(stores.Length == 1, "Captured Paint must contain exactly one target color store");
    var drawCount = drawInput.Verify(ShellCursorRewriter.Replace(drawInput.Input, cursorFixture, getter), semantic.CursorIndices, getter);
    var paintCount = paintInput.Verify(ColorStoreRewriter.Replace(paintInput.Input, colorFixture, setter), stores, setter);

    var controls = new List<string>();
    var missing = CapturedMethod.Clone(drawInput.Input);
    missing[semantic.CursorIndices[0]].operand = typeof(ForeignLayerFixture).GetField(nameof(ForeignLayerFixture.shellCursor));
    RejectCursor(missing, "missing-cursor-load");
    var extra = CapturedMethod.Clone(drawInput.Input);
    extra.Add(new CodeInstruction(OpCodes.Ldfld, cursorFixture));
    RejectCursor(extra, "extra-cursor-load");
    var foreignCursor = CapturedMethod.Clone(drawInput.Input);
    foreignCursor.Add(new CodeInstruction(OpCodes.Ldfld,
        typeof(ForeignLayerFixture).GetField(nameof(ForeignLayerFixture.shellCursor))));
    CapturedMethod.VerifyClone(foreignCursor,
        ShellCursorRewriter.Replace(foreignCursor, cursorFixture, getter).ToArray(), semantic.CursorIndices, getter);
    controls.Add("foreign-same-name-cursor-unmatched");
    var foreignColor = CapturedMethod.Clone(paintInput.Input);
    foreignColor.Add(new CodeInstruction(OpCodes.Stfld,
        typeof(ForeignShellFixture).GetField(nameof(ForeignShellFixture.color))));
    CapturedMethod.VerifyClone(foreignColor,
        ColorStoreRewriter.Replace(foreignColor, colorFixture, setter).ToArray(), stores, setter);
    controls.Add("foreign-same-name-color-unmatched");
    var missingColor = CapturedMethod.Clone(paintInput.Input);
    missingColor[stores[0]].operand = typeof(ForeignShellFixture).GetField(nameof(ForeignShellFixture.color));
    Check.Reject(() => ColorStoreRewriter.Replace(missingColor, colorFixture, setter).ToArray(),
        "Dyson paint method no longer contains a shell color store.");
    controls.Add("missing-color-store");

    foreach (var target in new[] { cursor, color })
    {
        var otherOwner = new TypeReference("", "ForeignOwner", module, module);
        var foreignScope = new TypeReference("", target.DeclaringType.Name, module,
            new AssemblyNameReference("ForeignAssembly", new Version(1, 0)));
        var foreignFieldType = new TypeReference(target.FieldType.Namespace, target.FieldType.Name, module,
            new AssemblyNameReference("ForeignFieldTypes", new Version(1, 0)));
        foreach (var reference in new[]
        {
            new FieldReference(target.Name, target.FieldType, otherOwner),
            new FieldReference(target.Name + "Other", target.FieldType, target.DeclaringType),
            new FieldReference(target.Name, module.TypeSystem.String, target.DeclaringType),
            new FieldReference(target.Name, target.FieldType, foreignScope),
            new FieldReference(target.Name, foreignFieldType, target.DeclaringType)
        })
        {
            var input = target == cursor ? drawInput : paintInput;
            Check.That(ReferenceEquals(reference, input.MapOperand(reference)), "Adapter matched an inexact field");
        }
    }
    controls.Add("adapter-rejects-foreign-owner-name-type-scope");
    controls.Add("adapter-rejects-same-name-foreign-field-type-scope");
    var source = draw.Body.Instructions.ToArray();
    SemanticNegative("unsigned-remainder", copy => copy[semantic.CursorIndices[0] + 1].OpCode = Mono.Cecil.Cil.OpCodes.Rem_Un);
    SemanticNegative("different-layer-local", copy => copy[semantic.CursorIndices[1] - 1].Operand = draw.Body.Variables[0]);
    SemanticNegative("zero-loop-initializer", copy => copy[semantic.CursorIndices[0] + 3].OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
    SemanticNegative("wrong-loop-target", copy => copy[semantic.CursorIndices[1] + 1].Operand = copy[semantic.CursorIndices[1] + 2]);
    SemanticNegative("missing-overlay-fallthrough", copy => copy[semantic.CursorIndices[1] + 3].OpCode = Mono.Cecil.Cil.OpCodes.Nop);
    drawInput.AssertRawUnchanged();
    paintInput.AssertRawUnchanged();
    AssertIsolation();
    var report = new
    {
        status = "passed", inputSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        moduleMvid = module.Mvid, patchesTransformed = new { cursor = drawCount, color = paintCount },
        drawModels = drawInput.Report(semantic.CursorIndices), paint = paintInput.Report(stores),
        semantics = new { sameLayerLocal = semantic.LayerLocal, firstFeedsSignedRem = true,
            secondFeedsSignedBltLoop = true, shellIndexStartsAtOne = true, overlayIsDirectFallthrough = true },
        negativeControls = new { passed = controls.Count, cases = controls },
        isolation = new { forbiddenAssembliesObserved = 0, observedAssemblyCount = loaded.Count,
            checkedAssemblyNames = new[] { "Assembly-CSharp*", "UnityEngine*", "BepInEx*", "SphereOpt" }, externalAssemblyResolution = "rejected" },
        limits = new[]
        {
            "Cecil reads captured bytes as data; game and plugin assemblies are never loaded or executed.",
            "Only target FieldReferences with validated owner, name, type and both owner/type scopes map to fixture FieldInfo because the helpers require it.",
            "Fixture fields and replacement MethodInfo are not game runtime types or production patch methods; no game ABI or behavior proof.",
            "Other operands retain identity, including Cecil branch/switch objects; real labels attach to actual branch destinations.",
            "Actual disjoint finally regions map to Harmony boundary blocks; other EH shapes fail closed.",
            "No Harmony patch application, IL emission, delegate compilation, Unity graphics test, or game execution."
        }
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Check.That(System.Text.Encoding.UTF8.GetByteCount(json + Environment.NewLine) < 16384,
        "Success report exceeded its fixed 16 KiB bound");
    AssertIsolation();
    if (args.Length == 4) File.WriteAllText(args[3], json + Environment.NewLine);
    Console.WriteLine(json);
    return 0;

    void AssertIsolation()
    {
        lock (loaded) Check.That(!loaded.Any(name =>
            name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SphereOpt", StringComparison.OrdinalIgnoreCase)), "Forbidden game or plugin assembly loaded");
    }
    void RejectCursor(List<CodeInstruction> input, string name)
    {
        Check.Reject(() => ShellCursorRewriter.Replace(input, cursorFixture, getter).ToArray(),
            "Unsupported Dyson shell render-loop contract: expected two shellCursor reads.");
        controls.Add(name);
    }
    void SemanticNegative(string name, Action<Mono.Cecil.Cil.Instruction[]> mutate)
    {
        var copy = DrawContract.CloneGraph(source);
        mutate(copy);
        Check.Reject(() => DrawContract.Validate(copy, draw.Body.Variables.ToArray(), cursor));
        controls.Add(name);
    }
}
catch (Exception exception)
{
    var message = exception.Message;
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", error = message[..Math.Min(message.Length, 1500)] }));
    return 1;
}

internal sealed class RejectResolution : IAssemblyResolver
{
    public AssemblyDefinition Resolve(AssemblyNameReference name) => throw new InvalidOperationException("External assembly resolution is forbidden");
    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) => Resolve(name);
    public void Dispose() { }
}

// These local types adapt the helper API only. No fixture method is executed.
internal sealed class LayerFixture { public int shellCursor = 0; }
internal sealed class ForeignLayerFixture { public int shellCursor = 0; }
internal readonly record struct FixtureColor(byte R, byte G, byte B, byte A);
internal sealed class ShellFixture { public FixtureColor color = default; }
internal sealed class ForeignShellFixture { public FixtureColor color = default; }
internal static class FixtureHooks
{
    public static int Cursor(LayerFixture layer) => throw new NotSupportedException("Metadata-only fixture");
    public static void Color(ShellFixture shell, FixtureColor color) => throw new NotSupportedException("Metadata-only fixture");
}
