using System.Reflection;
using HarmonyLib;
using Mono.Cecil;

namespace SphereOpt.InstructionChecks;

internal static class PatchOrderChecks
{
    internal const string DspoOwner = "com.Selsion.DSPOptimizations";
    internal const string PaintPatchType = "DSPOptimizations.ShellShaderVarOpt/Patch";
    internal const string PaintPatchMethod = "UpdateShellColourPatch2";

    internal static object Run(ProofMetadata plugin, ProofMetadata dspo)
    {
        if (plugin == null) return new { status = "not-requested", reason = "Supply --plugin to check compiled candidate metadata and Harmony sorting" };
        var candidate = plugin.Assembly;
        Check.That(candidate.Name.Name == "SphereOpt", "--plugin must name the candidate SphereOpt assembly");
        var patch = ProofMetadata.Method(candidate, "SphereOpt.Patch_DysonShell", "UIDysonBrush_Paint__OnUpdate");
        Check.That(patch.IsStatic && patch.HasBody && patch.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyTranspiler"),
            "Candidate paint method must be a compiled static Harmony transpiler");
        Check.That(patch.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch" &&
            a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value is TypeReference t &&
            t.FullName == "UIDysonBrush_Paint" && Equals(a.ConstructorArguments[1].Value, "_OnUpdate")),
            "Candidate paint transpiler must target UIDysonBrush_Paint._OnUpdate");
        Check.That(patch.Body.Instructions.Count(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "SphereOpt.ColorStoreRewriter" && m.Name == "Replace" &&
            ReferenceEquals(m.DeclaringType.Scope, candidate.MainModule)) == 1,
            "Compiled paint patch must call the real ColorStoreRewriter.Replace");
        var owner = Owner(candidate, "SphereOpt.SphereOpt");
        var actual = Ordering(patch);
        Check.That(actual.After.Contains(DspoOwner, StringComparer.Ordinal),
            "Compiled candidate paint transpiler is missing HarmonyAfter(\"com.Selsion.DSPOptimizations\")");
        var dsp = dspo == null ? new Order(Priority.Normal, [], []) :
            Ordering(ProofMetadata.Method(dspo.Assembly, PaintPatchType, PaintPatchMethod));
        if (dspo != null) Check.That(Owner(dspo.Assembly, "DSPOptimizations.Plugin") == DspoOwner,
            "Installed DSPOptimizations owner does not match the ordering ID");

        var sortedCases = new List<object>();
        foreach (var candidateFirst in new[] { true, false })
        {
            var result = Sort(actual, dsp, owner, candidateFirst);
            Check.That(result.SequenceEqual(new[] { "DSPOptimizations", "candidate" }),
                "Real Harmony sorter did not put DSPOptimizations before the candidate");
            sortedCases.Add(new { registrationOrder = candidateFirst ? "candidate,DSPOptimizations" : "DSPOptimizations,candidate",
                sortedOrder = result });
        }
        // Remove only the actual assembly's dependency. This must restore the bad
        // candidate-first order, so a copied fixture attribute cannot make us pass.
        var withoutAfter = actual with { After = actual.After.Where(id => id != DspoOwner).ToArray() };
        var counterfactual = Sort(withoutAfter, dsp, owner, candidateFirst: true);
        Check.That(counterfactual.SequenceEqual(new[] { "candidate", "DSPOptimizations" }),
            "Removing the compiled HarmonyAfter did not expose the ordering fault");
        return new
        {
            status = "passed", plugin = plugin.Identity(), methodToken = $"0x{patch.MetadataToken.ToInt32():x8}",
            candidateOwner = owner, dspoOwner = DspoOwner, priority = actual.Priority,
            before = actual.Before, after = actual.After, registrationCases = sortedCases,
            counterfactualWithoutActualAfter = counterfactual,
            dspoOrderingSource = dspo == null ? "reviewed source default priority/no dependencies; installed DLL not supplied" : "installed DLL metadata",
            sorter = "HarmonyX 2.5.5 PatchProcessor.GetSortedPatchMethods; attribute-free local MethodInfo adapters",
            limits = "Two-patch sorting only; no candidate/game loading, real patch installation, loader or other-mod ordering proof"
        };
    }

    private static string Owner(AssemblyDefinition assembly, string type) => assembly.MainModule.Types.Single(t => t.FullName == type)
        .CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin").ConstructorArguments[0].Value as string
        ?? throw new InvalidOperationException("Missing plugin owner metadata");

    private sealed record Order(int Priority, string[] Before, string[] After);
    private static Order Ordering(MethodDefinition method)
    {
        Check.That(!method.DeclaringType.CustomAttributes.Any(a => a.AttributeType.FullName is
            "HarmonyLib.HarmonyBefore" or "HarmonyLib.HarmonyAfter" or "HarmonyLib.HarmonyPriority"),
            "Class-level ordering requires a separate metadata merge adapter");
        var known = new[] { "HarmonyLib.HarmonyBefore", "HarmonyLib.HarmonyAfter", "HarmonyLib.HarmonyPriority" };
        foreach (var attribute in method.CustomAttributes.Where(a => known.Contains(a.AttributeType.FullName)))
            Check.That(attribute.AttributeType.Scope.Name == "0Harmony" && attribute.ConstructorArguments.Count == 1 &&
                attribute.Fields.Count == 0 && attribute.Properties.Count == 0, "Unsupported ordering attribute encoding");
        string[] Strings(string name)
        {
            var values = method.CustomAttributes.Where(a => a.AttributeType.FullName == name).ToArray();
            Check.That(values.Length <= 1, "Duplicate ordering attribute");
            return values.Length == 0 ? [] : ((CustomAttributeArgument[])values[0].ConstructorArguments[0].Value)
                .Select(a => (string)a.Value).ToArray();
        }
        var priority = method.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPriority").ToArray();
        Check.That(priority.Length <= 1, "Duplicate priority attribute");
        return new Order(priority.Length == 0 ? Priority.Normal : (int)priority[0].ConstructorArguments[0].Value,
            Strings("HarmonyLib.HarmonyBefore"), Strings("HarmonyLib.HarmonyAfter"));
    }

    private static string[] Sort(Order candidate, Order dsp, string owner, bool candidateFirst)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var candidateMethod = typeof(OrderFixtures).GetMethod(nameof(OrderFixtures.Candidate), flags);
        var dspMethod = typeof(OrderFixtures).GetMethod(nameof(OrderFixtures.Dspo), flags);
        var registrations = candidateFirst ? new[] { (candidateMethod, owner, candidate), (dspMethod, DspoOwner, dsp) } :
            new[] { (dspMethod, DspoOwner, dsp), (candidateMethod, owner, candidate) };
        var patches = registrations.Select((entry, index) => new Patch(entry.Item1, index, entry.Item2,
            entry.Item3.Priority, entry.Item3.Before, entry.Item3.After, false)).ToArray();
        return PatchProcessor.GetSortedPatchMethods(typeof(OrderFixtures).GetMethod(nameof(OrderFixtures.Target), flags), patches)
            .Select(method => method == dspMethod ? "DSPOptimizations" : method == candidateMethod ? "candidate" : "unexpected").ToArray();
    }

    // No Harmony attributes: every sorting option comes from Cecil-read inputs.
    private static class OrderFixtures
    {
        internal static void Target() => throw new NotSupportedException("Sorting metadata only");
        internal static void Candidate() => throw new NotSupportedException("Sorting metadata only");
        internal static void Dspo() => throw new NotSupportedException("Sorting metadata only");
    }
}
