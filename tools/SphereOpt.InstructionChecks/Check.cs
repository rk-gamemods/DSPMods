using Mono.Cecil;

namespace SphereOpt.InstructionChecks;

internal static class Check
{
    internal static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Reject(Action action, string expectedMessage = null)
    {
        try { action(); }
        catch (InvalidOperationException exception) when (expectedMessage == null || exception.Message == expectedMessage) { return; }
        throw new InvalidOperationException("Negative control did not produce its expected rejection");
    }

    internal static MethodDefinition Method(ModuleDefinition module, string owner, string name, params string[] parameters)
    {
        var method = module.Types.Single(t => t.FullName == owner).Methods.Single(m => m.Name == name);
        That(!method.IsStatic && method.ReturnType.FullName == "System.Void" && method.HasBody &&
            method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters), "Unexpected captured method signature");
        return method;
    }

    internal static FieldDefinition Field(ModuleDefinition module, string owner, string name, string type)
    {
        var field = module.Types.Single(t => t.FullName == owner).Fields.Single(f => f.Name == name);
        That(!field.IsStatic && field.FieldType.FullName == type, "Unexpected captured field signature");
        return field;
    }

    internal static bool Matches(object operand, FieldDefinition field) => operand is FieldReference reference &&
        reference.Name == field.Name && reference.DeclaringType.FullName == field.DeclaringType.FullName &&
        reference.FieldType.FullName == field.FieldType.FullName &&
        ReferenceEquals(reference.FieldType.Scope, field.FieldType.Scope) &&
        ReferenceEquals(reference.Module, field.Module) && ReferenceEquals(reference.DeclaringType.Scope, field.Module) &&
        ReferenceEquals(reference.Resolve(), field); // Only resolve an already validated same-module owner.

    internal static bool Matches(object operand, MethodDefinition method) => operand is MethodReference reference &&
        reference.Name == method.Name && reference.DeclaringType.FullName == method.DeclaringType.FullName &&
        ReferenceEquals(reference.Module, method.Module) && ReferenceEquals(reference.DeclaringType.Scope, method.Module) &&
        reference.HasThis == method.HasThis && reference.ExplicitThis == method.ExplicitThis &&
        reference.CallingConvention == method.CallingConvention &&
        reference.GenericParameters.Count == method.GenericParameters.Count &&
        SameType(reference.ReturnType, method.ReturnType) && reference.Parameters.Count == method.Parameters.Count &&
        reference.Parameters.Select((p, i) => SameType(p.ParameterType, method.Parameters[i].ParameterType)).All(v => v) &&
        ReferenceEquals(reference.Resolve(), method);

    private static bool SameType(TypeReference left, TypeReference right) =>
        left.FullName == right.FullName && ReferenceEquals(left.Scope, right.Scope);
}
