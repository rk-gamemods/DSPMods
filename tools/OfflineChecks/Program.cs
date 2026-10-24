using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DspOfflineChecks;

record Finding(string Kind, string Subject, string Detail);
record CheckReport(string Status, int CheckedReferences, int CheckedPatchTargets,
    List<Finding> Errors, string[] Limits);

static class Program
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly string[] Limits = [
        "Metadata inspection only: game and plugin code were not executed.",
        "Transpiler instruction matching/application is not validated.",
        "Native Unity behavior, shaders, visuals, saves and performance require runtime tests."
    ];

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "self-test") return SelfTest();
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
                throw new ArgumentException("Usage: check --managed DIR --mod FILE --report FILE | self-test");
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
                $"{report.CheckedPatchTargets} Harmony targets, {report.Errors.Count} errors.");
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
        return new(errors.Count == 0 ? "PASS_WITH_LIMITATIONS" : "FAIL", references, patches, errors, Limits);
    }

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
            var targetMethod = new MethodDefinition("Work", MethodAttributes.Public | MethodAttributes.Static,
                game.MainModule.TypeSystem.Void);
            targetMethod.Parameters.Add(new("amount", ParameterAttributes.None, game.MainModule.TypeSystem.Int32));
            targetMethod.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            target.Methods.Add(targetMethod);
            game.Write(gameFile);
            foreach (var test in new[] { ("Work", "amount", false), ("Missing", "amount", true), ("Work", "unknown", true) })
            {
                var modFile = Path.Combine(root, "FixtureMod.dll");
                using var mod = AssemblyDefinition.CreateAssembly(new("FixtureMod", new(1, 0)), "FixtureMod", ModuleKind.Dll);
                var module = mod.MainModule;
                var patchType = new TypeDefinition("Fixture", "Patches", TypeAttributes.Public, module.TypeSystem.Object);
                module.Types.Add(patchType);
                var prefix = new MethodDefinition("Prefix", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
                prefix.Parameters.Add(new(test.Item2, ParameterAttributes.None, module.TypeSystem.Int32));
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
            return 0;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
