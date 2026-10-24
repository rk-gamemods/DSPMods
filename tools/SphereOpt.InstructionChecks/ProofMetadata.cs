using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ReflectionAssembly = System.Reflection.Assembly;

namespace SphereOpt.InstructionChecks;

internal sealed class ProofMetadata : IDisposable
{
    internal readonly string Path;
    internal readonly byte[] Bytes;
    internal readonly AssemblyDefinition Assembly;
    internal ProofMetadata(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Bytes = File.ReadAllBytes(Path);
        Assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(Bytes, writable: false), new ReaderParameters
        { AssemblyResolver = new RejectResolution(), ReadSymbols = false, ReadingMode = ReadingMode.Deferred });
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal object Identity() => new { path = Path, sha256 = Hash(Bytes), mvid = Assembly.MainModule.Mvid,
        assemblyName = Assembly.Name.Name, assemblyVersion = Assembly.Name.Version.ToString() };
    public void Dispose() => Assembly.Dispose();

    internal static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Descend);
    private static IEnumerable<TypeDefinition> Descend(TypeDefinition type) =>
        new[] { type }.Concat(type.NestedTypes.SelectMany(Descend));

    internal static MethodDefinition Method(AssemblyDefinition assembly, string type, string name) =>
        Types(assembly.MainModule).Single(t => t.FullName == type).Methods.Single(m => m.Name == name);

    // Deliberately omits scopes. This checks narrow transform-body equivalence across
    // fixture/.NET-host substitution, not type compatibility or binary equivalence.
    internal static string[] Normalize(MethodDefinition method)
    {
        var instructions = method.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        return instructions.Select(i =>
        {
            var opcode = i.OpCode.Name;
            if (opcode.EndsWith(".s", StringComparison.Ordinal)) opcode = opcode[..^2];
            var operand = i.Operand switch
            {
                null => "", MethodReference m => Canonical(m.FullName), FieldReference f => Canonical(f.FullName),
                TypeReference t => Canonical(t.FullName), VariableDefinition v => "V_" + v.Index,
                Instruction target => "@" + Target(target),
                Instruction[] targets => string.Join(",", targets.Select(Target)), _ => i.Operand.ToString()
            };
            return opcode + (operand.Length == 0 ? "" : " " + operand);
        }).ToArray();

        int Target(Instruction target)
        {
            while (target?.OpCode.Code == Code.Nop) target = target.Next;
            return Array.IndexOf(instructions, target);
        }
    }

    internal static string Canonical(string text) => text
        .Replace("SphereOpt.InstructionChecks.SegmentRendererFixture", "DysonSphereSegmentRenderer", StringComparison.Ordinal)
        .Replace("ShellFixture", "DysonShell", StringComparison.Ordinal)
        .Replace("FixtureColor", "UnityEngine.Color32", StringComparison.Ordinal);

    internal static object[] CompiledSources() => ReflectionAssembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(name => name.StartsWith("ProofSource.", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal)
        .Select(name =>
        {
            using var stream = ReflectionAssembly.GetExecutingAssembly().GetManifestResourceStream(name);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return (object)new { name, sha256 = Hash(bytes.ToArray()) };
        }).ToArray();
}
