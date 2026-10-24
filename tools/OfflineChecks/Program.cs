using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DspOfflineChecks;

record Finding(string Kind, string Subject, string Detail);
record CheckReport(string Status, int CheckedReferences, int CheckedPatchTargets, int CheckedInstructionContracts,
    List<Finding> Errors, string[] Limits);

static class Program
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly string[] Limits = [
        "Metadata inspection only: game and plugin code were not executed.",
        "Only reported instruction contracts were checked; Harmony patch application was not executed.",
        "Native Unity behavior, shaders, visuals, saves and performance require runtime tests."
    ];

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "self-test") return SelfTest();
            if (args.Length == 2 && args[0] == "identity")
            {
                using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
                var plugin = Types(assembly.MainModule.Types).SelectMany(t => t.CustomAttributes)
                    .Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                Console.WriteLine(JsonSerializer.Serialize(new {
                    assembly = assembly.Name.Name, assemblyVersion = assembly.Name.Version.ToString(),
                    guid = plugin.ConstructorArguments[0].Value,
                    name = plugin.ConstructorArguments[1].Value,
                    version = plugin.ConstructorArguments[2].Value
                }, JsonOptions));
                return 0;
            }
            if (args.Length == 4 && args[0] == "instructions")
            {
                using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
                var type = Types(assembly.MainModule.Types).Single(t => t.FullName == args[2]);
                var method = type.Methods.Single(m => m.Name == args[3]);
                Console.WriteLine(JsonSerializer.Serialize(new {
                    method = method.FullName,
                    locals = method.Body.Variables.Select(v => new { index = v.Index, type = v.VariableType.FullName }),
                    instructions = method.Body.Instructions.Select(i => new {
                        offset = i.Offset, opcode = i.OpCode.Name, operand = i.Operand?.ToString()
                    })
                }, JsonOptions));
                return 0;
            }
            if (args.Length == 3 && args[0] == "describe")
            {
                using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
                var type = Types(assembly.MainModule.Types).Single(t => t.FullName == args[2]);
                Console.WriteLine(JsonSerializer.Serialize(new {
                    type = type.FullName,
                    fields = type.Fields.Select(f => new { name = f.Name, access = f.Attributes.ToString(), type = f.FieldType.FullName }),
                    methods = type.Methods.Select(m => m.FullName)
                }, JsonOptions));
                return 0;
            }
            if (args.Length == 0 || args[0] != "check")
                throw new ArgumentException("Usage: check --managed DIR --mod FILE --report FILE | identity FILE | describe FILE TYPE | instructions FILE TYPE METHOD | self-test");
            string Option(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1])
                    : throw new ArgumentException("Missing " + name);
            }
            var report = Check(Option("--managed"), Option("--mod"));
            var output = Option("--report");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(report, JsonOptions) + "\n");
            Console.WriteLine($"{report.Status}: {report.CheckedReferences} references, " +
                $"{report.CheckedPatchTargets} Harmony targets, {report.CheckedInstructionContracts} instruction contracts, {report.Errors.Count} errors.");
            foreach (var error in report.Errors.Take(20))
                Console.WriteLine($"{error.Kind}: {error.Subject}: {error.Detail}");
            Console.WriteLine("Full report: " + output);
            return report.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }

    static string? Scope(TypeReference type)
    {
        while (type is TypeSpecification specification) type = specification.ElementType;
        while (type.DeclaringType != null) type = type.DeclaringType;
        return (type.Scope as AssemblyNameReference)?.Name;
    }

    static string PlainType(TypeReference type) => type is ByReferenceType reference
        ? reference.ElementType.FullName : type.FullName;

    static IEnumerable<CustomAttribute> PatchAttributes(ICustomAttributeProvider item) =>
        item.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");

    internal static CheckReport Check(string managed, string plugin)
    {
        var errors = new List<Finding>();
        var references = 0;
        var patches = 0;
        var contracts = 0;
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        resolver.AddSearchDirectory(Path.GetDirectoryName(plugin)!);
        var parameters = new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred };
        using var mod = AssemblyDefinition.ReadAssembly(plugin, parameters);
        var relevant = Directory.EnumerateFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != "mscorlib" && n != "netstandard" && !n!.StartsWith("System")
                && !n.StartsWith("Microsoft") && !n.StartsWith("Mono")).ToHashSet();
        foreach (var assembly in mod.MainModule.AssemblyReferences.Where(a =>
                     a.Name is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "UnityEngine"
                     || a.Name.StartsWith("UnityEngine.", StringComparison.Ordinal)))
        {
            relevant.Add(assembly.Name);
            if (!File.Exists(Path.Combine(managed, assembly.Name + ".dll")))
                errors.Add(new("MissingAssembly", assembly.Name,
                    "Declared game/Unity dependency is absent from captured assemblies."));
        }

        foreach (var type in mod.MainModule.GetTypeReferences().Where(t => relevant.Contains(Scope(t))))
        {
            references++;
            try
            {
                if (type.Resolve() == null) errors.Add(new("MissingType", type.FullName, Scope(type) ?? "unknown"));
            }
            catch (Exception ex) { errors.Add(new("UnresolvedType", type.FullName, ex.Message)); }
        }
        foreach (var member in mod.MainModule.GetMemberReferences().Where(m => relevant.Contains(Scope(m.DeclaringType))))
        {
            if (member is not MethodReference && member is not FieldReference) continue;
            references++;
            try
            {
                object? resolved = member switch
                {
                    MethodReference method => method.Resolve(),
                    FieldReference field => field.Resolve(),
                    _ => null
                };
                if (resolved == null) errors.Add(new("MissingMember", member.FullName, "Not found in captured assemblies."));
            }
            catch (Exception ex) { errors.Add(new("UnresolvedMember", member.FullName, ex.Message)); }
        }

        foreach (var type in Types(mod.MainModule.Types))
        foreach (var method in type.Methods)
        {
            if (method.HasBody)
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not GenericInstanceMethod accessor ||
                    accessor.DeclaringType.FullName != "HarmonyLib.AccessTools" || accessor.Name != "FieldRefAccess") continue;
                references++;
                if (accessor.GenericArguments.Count != 2 || accessor.Parameters.Count != 1 ||
                    accessor.Parameters[0].ParameterType.FullName != "System.String" ||
                    instruction.Previous?.OpCode != OpCodes.Ldstr)
                {
                    errors.Add(new("UnsupportedFieldAccessor", method.FullName,
                        "Requires a typed instance FieldRefAccess<T,F> with a literal field name."));
                    continue;
                }
                var fieldName = (string)instruction.Previous.Operand;
                try
                {
                    var owner = accessor.GenericArguments[0].Resolve();
                    var field = owner?.Fields.SingleOrDefault(f => f.Name == fieldName);
                    if (field == null || field.IsStatic)
                        errors.Add(new("MissingReflectedField", method.FullName, accessor.GenericArguments[0].FullName + "." + fieldName));
                    else if (field.FieldType.FullName != accessor.GenericArguments[1].FullName)
                        errors.Add(new("ReflectedFieldTypeMismatch", method.FullName,
                            $"{fieldName}: expected {accessor.GenericArguments[1].FullName}, captured {field.FieldType.FullName}."));
                }
                catch (Exception ex) { errors.Add(new("UnresolvedReflectedField", method.FullName, ex.Message)); }
            }
            var attributes = PatchAttributes(type).Concat(PatchAttributes(method)).ToArray();
            var isPatch = method.CustomAttributes.Any(a => a.AttributeType.Namespace == "HarmonyLib" &&
                a.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyTranspiler" or "HarmonyFinalizer")
                || method.Name is "Prefix" or "Postfix" or "Transpiler" or "Finalizer";
            if (!isPatch || attributes.Length == 0) continue;
            TypeReference? declaringType = null;
            string? methodName = null;
            string[]? argumentTypes = null;
            var unsupported = false;
            foreach (var attribute in attributes)
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is TypeReference targetType) declaringType = targetType;
                else if (argument.Value is string targetName) methodName = targetName;
                else if (argument.Value is CustomAttributeArgument[] array &&
                    argument.Type is ArrayType arrayType && arrayType.ElementType.FullName == "System.Type")
                    argumentTypes = array.Select(a => ((TypeReference)a.Value).FullName).ToArray();
                else unsupported = true;
            }
            patches++;
            if (unsupported || declaringType == null || methodName == null)
            {
                errors.Add(new("UnsupportedPatchDeclaration", method.FullName,
                    "Requires manual review; checker supports normal methods with constructor-based type/name/signature."));
                continue;
            }
            TypeDefinition? target = null;
            try { target = declaringType.Resolve(); }
            catch (Exception ex) { errors.Add(new("UnresolvedPatchType", method.FullName, ex.Message)); }
            if (target == null)
            {
                errors.Add(new("MissingPatchType", method.FullName, declaringType.FullName));
                continue;
            }
            var candidates = target.Methods.Where(m => m.Name == methodName &&
                (argumentTypes == null || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argumentTypes)))
                .ToArray();
            if (candidates.Length != 1)
            {
                errors.Add(new(candidates.Length == 0 ? "MissingPatchTarget" : "AmbiguousPatchTarget", method.FullName,
                    $"{declaringType.FullName}.{methodName}: {candidates.Length} matching methods."));
                continue;
            }
            if (method.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyTranspiler")) continue;
            foreach (var injected in method.Parameters)
            {
                var name = injected.Name;
                if (name.StartsWith("___"))
                {
                    var field = target.Fields.FirstOrDefault(f => f.Name == name[3..]);
                    if (field == null) errors.Add(new("MissingInjectedField", method.FullName, name));
                    continue;
                }
                if (name is "__instance" or "__result")
                {
                    if (name == "__instance" && candidates[0].IsStatic)
                    {
                        // HarmonyX 2.5.5 emits ldnull for a static original.
                        if (injected.ParameterType is ByReferenceType || injected.ParameterType.IsValueType)
                            errors.Add(new("StaticTargetInstanceType", method.FullName, "Static __instance requires a by-value reference type."));
                        continue;
                    }
                    var expected = name == "__instance" ? target.FullName : candidates[0].ReturnType.FullName;
                    if (PlainType(injected.ParameterType) != expected && PlainType(injected.ParameterType) != "System.Object")
                        errors.Add(new("InjectedTypeMismatch", method.FullName, $"{name}: expected {expected}."));
                    continue;
                }
                if (name is "__state" or "__args" or "__originalMethod" or "__runOriginal" or "__exception") continue;
                var original = name.StartsWith("__") && int.TryParse(name[2..], out var ordinal)
                    ? candidates[0].Parameters.ElementAtOrDefault(ordinal)
                    : candidates[0].Parameters.FirstOrDefault(p => p.Name == name);
                if (original == null) errors.Add(new("MissingInjectedArgument", method.FullName, name));
                else if (PlainType(injected.ParameterType) != PlainType(original.ParameterType)
                         && PlainType(injected.ParameterType) != "System.Object")
                    errors.Add(new("InjectedTypeMismatch", method.FullName,
                        $"{name}: patch {injected.ParameterType.FullName}, target {original.ParameterType.FullName}."));
            }
        }
        if (Types(mod.MainModule.Types).Any(t => t.FullName == "SphereOpt.ShellCursorRewriter"))
        {
            contracts++;
            CheckInstructionContract(resolver, "DysonSphereSegmentRenderer", "DrawModels", "DysonSphereLayer", "shellCursor", OpCodes.Ldfld, 2, errors);
        }
        if (Types(mod.MainModule.Types).Any(t => t.FullName == "SphereOpt.ColorStoreRewriter"))
        {
            contracts++;
            CheckInstructionContract(resolver, "UIDysonBrush_Paint", "_OnUpdate", "DysonShell", "color", OpCodes.Stfld, 1, errors);
        }
        return new(errors.Count == 0 ? "PASS_WITH_LIMITATIONS" : "FAIL", references, patches, contracts, errors, Limits);
    }

    static void CheckInstructionContract(IAssemblyResolver resolver, string owner, string methodName,
        string fieldOwner, string fieldName, OpCode opcode, int expected, List<Finding> errors)
    {
        try
        {
            var game = resolver.Resolve(new AssemblyNameReference("Assembly-CSharp", new Version(0, 0)));
            var method = Types(game.MainModule.Types).Single(t => t.FullName == owner).Methods.Single(m => m.Name == methodName);
            var actual = CountFieldInstructions(method, fieldOwner, fieldName, opcode);
            if (actual != expected) errors.Add(new("InstructionContractMismatch", method.FullName,
                $"Expected {expected} {opcode.Name} {fieldOwner}.{fieldName}, found {actual}."));
        }
        catch (Exception ex) { errors.Add(new("UnresolvedInstructionContract", owner + "." + methodName, ex.Message)); }
    }

    static int CountFieldInstructions(MethodDefinition method, string owner, string field, OpCode opcode) =>
        method.Body.Instructions.Count(i => i.OpCode == opcode && i.Operand is FieldReference reference &&
            reference.DeclaringType.FullName == owner && reference.Name == field);

    static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsp-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var gameFile = Path.Combine(root, "FixtureGame.dll");
            using var game = AssemblyDefinition.CreateAssembly(new("FixtureGame", new(1, 0)), "FixtureGame", ModuleKind.Dll);
            var target = new TypeDefinition("Fixture", "Target", TypeAttributes.Public, game.MainModule.TypeSystem.Object);
            game.MainModule.Types.Add(target);
            target.Fields.Add(new("privateValue", FieldAttributes.Private, game.MainModule.TypeSystem.Int32));
            var targetMethod = new MethodDefinition("Work", MethodAttributes.Public | MethodAttributes.Static,
                game.MainModule.TypeSystem.Void);
            targetMethod.Parameters.Add(new("amount", ParameterAttributes.None, game.MainModule.TypeSystem.Int32));
            targetMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            target.Methods.Add(targetMethod);
            game.Write(gameFile);
            foreach (var test in new[] { ("Work", "amount", false, 0), ("Missing", "amount", true, 0), ("Work", "unknown", true, 0),
                         ("Work", "__instance", false, 1), ("Work", "__instance", true, 2), ("Work", "__instance", true, 3) })
            {
                var modFile = Path.Combine(root, "FixtureMod.dll");
                using var mod = AssemblyDefinition.CreateAssembly(new("FixtureMod", new(1, 0)), "FixtureMod", ModuleKind.Dll);
                var module = mod.MainModule;
                var patchType = new TypeDefinition("Fixture", "Patches", TypeAttributes.Public, module.TypeSystem.Object);
                module.Types.Add(patchType);
                var prefix = new MethodDefinition("Prefix", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
                TypeReference injectionType = test.Item4 switch {
                    1 => module.TypeSystem.Object,
                    3 => new ByReferenceType(module.TypeSystem.Object),
                    _ => module.TypeSystem.Int32
                };
                prefix.Parameters.Add(new(test.Item2, ParameterAttributes.None, injectionType));
                prefix.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
                prefix.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                patchType.Methods.Add(prefix);
                var fakeHarmony = new AssemblyNameReference("FakeHarmony", new(1, 0));
                var attributeType = new TypeReference("HarmonyLib", "HarmonyPatch", module, fakeHarmony);
                var constructor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
                var systemType = new TypeReference("System", "Type", module, module.TypeSystem.CoreLibrary);
                constructor.Parameters.Add(new(systemType));
                constructor.Parameters.Add(new(module.TypeSystem.String));
                var attribute = new CustomAttribute(constructor);
                attribute.ConstructorArguments.Add(new(systemType, module.ImportReference(target)));
                attribute.ConstructorArguments.Add(new(module.TypeSystem.String, test.Item1));
                prefix.CustomAttributes.Add(attribute);
                mod.Write(modFile);
                var report = Check(root, modFile);
                if ((report.Errors.Count > 0) != test.Item3 || report.CheckedPatchTargets != 1)
                    throw new InvalidOperationException("Self-test mismatch: " + JsonSerializer.Serialize(report, JsonOptions));
                Console.WriteLine($"PASS: {test.Item1}/{test.Item2}, expected rejection={test.Item3}");
            }
            foreach (var assemblyName in new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "UnityEngine", "UnityEngine.CoreModule" })
            {
                var modFile = Path.Combine(root, "MissingAssemblyFixtureMod.dll");
                using var mod = AssemblyDefinition.CreateAssembly(new("MissingAssemblyFixtureMod", new(1, 0)),
                    "MissingAssemblyFixtureMod", ModuleKind.Dll);
                var module = mod.MainModule;
                var missing = new AssemblyNameReference(assemblyName, new(1, 0));
                module.AssemblyReferences.Add(missing);
                var fixture = new TypeDefinition("Fixture", "MissingAssemblyReference", TypeAttributes.Public,
                    module.TypeSystem.Object);
                fixture.Fields.Add(new FieldDefinition("value", FieldAttributes.Public,
                    new TypeReference("Fixture", "MissingGameType", module, missing)));
                module.Types.Add(fixture);
                mod.Write(modFile);
                var report = Check(root, modFile);
                if (report.Status != "FAIL" || report.CheckedReferences == 0 ||
                    !report.Errors.Any(e => e.Kind == "MissingAssembly" && e.Subject == assemblyName))
                    throw new InvalidOperationException("Missing-assembly self-test mismatch: " +
                        JsonSerializer.Serialize(report, JsonOptions));
                Console.WriteLine($"PASS: missing {assemblyName}, expected rejection=True");
            }
            foreach (var test in new[] { ("privateValue", false, ""), ("missing", false, "MissingReflectedField"),
                         ("privateValue", true, "ReflectedFieldTypeMismatch") })
            {
                var modFile = Path.Combine(root, "FieldAccessorMod.dll");
                using var mod = AssemblyDefinition.CreateAssembly(new("FieldAccessorMod", new(1, 0)), "FieldAccessorMod", ModuleKind.Dll);
                var module = mod.MainModule;
                var fixture = new TypeDefinition("Fixture", "Accessors", TypeAttributes.Public, module.TypeSystem.Object);
                module.Types.Add(fixture);
                var method = new MethodDefinition("Initialize", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
                fixture.Methods.Add(method);
                var accessTools = new TypeReference("HarmonyLib", "AccessTools", module, new AssemblyNameReference("FakeHarmony", new(1, 0)));
                var declaration = new MethodReference("FieldRefAccess", module.TypeSystem.Object, accessTools);
                declaration.GenericParameters.Add(new("T", declaration));
                declaration.GenericParameters.Add(new("F", declaration));
                declaration.Parameters.Add(new(module.TypeSystem.String));
                var accessor = new GenericInstanceMethod(declaration);
                accessor.GenericArguments.Add(module.ImportReference(target));
                accessor.GenericArguments.Add(test.Item2 ? module.TypeSystem.String : module.TypeSystem.Int32);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, test.Item1));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Call, accessor));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                mod.Write(modFile);
                var report = Check(root, modFile);
                if (test.Item3 == "" ? report.Errors.Count != 0 : !report.Errors.Any(e => e.Kind == test.Item3))
                    throw new InvalidOperationException("Reflected-field self-test mismatch: " + JsonSerializer.Serialize(report, JsonOptions));
                Console.WriteLine($"PASS: reflected {test.Item1}, wrongType={test.Item2}, expected={test.Item3}");
            }
            var instructionFixture = new MethodDefinition("RenderFixture", MethodAttributes.Static, game.MainModule.TypeSystem.Void);
            foreach (var op in new[] { OpCodes.Ldfld, OpCodes.Ldfld, OpCodes.Stfld })
                instructionFixture.Body.Instructions.Add(Instruction.Create(op, target.Fields[0]));
            if (CountFieldInstructions(instructionFixture, target.FullName, "privateValue", OpCodes.Ldfld) != 2 ||
                CountFieldInstructions(instructionFixture, target.FullName, "other", OpCodes.Ldfld) != 0 ||
                CountFieldInstructions(instructionFixture, target.FullName, "privateValue", OpCodes.Stfld) != 1)
                throw new InvalidOperationException("Instruction contract counting self-test failed.");
            Console.WriteLine("PASS: instruction contracts distinguish field and opcode");
            return 0;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
