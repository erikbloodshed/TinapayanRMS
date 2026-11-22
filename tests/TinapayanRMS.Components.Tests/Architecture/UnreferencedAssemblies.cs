using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// The app's and the two servers' assemblies (TinapayanRMS.Maui, TinapayanRMS.RecipeApi, TinapayanRMS.Mcp), which this
/// project builds but doesn't reference: the app's is Windows only, and the servers' would bring in what only they use.
/// Their IL is read from the files their builds make (the project file names each in an AssemblyMetadata attribute),
/// without loading them, so the methods they call are known by name and signature alone.
/// </summary>
internal static class UnreferencedAssemblies
{
    private const string Key = "ReadAssembly:";

    /// <summary>Each one's file, by assembly name.</summary>
    public static IReadOnlyDictionary<string, string> Files { get; } =
        typeof(UnreferencedAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(Key, StringComparison.Ordinal))
            .ToDictionary(attribute => attribute.Key[Key.Length..], attribute => attribute.Value!, StringComparer.Ordinal);

    /// <summary>A method called, as its signature names it: "System.Decimal", "System.Double", generic arguments filled in.</summary>
    public sealed record CalledMethod(string Name, string ReturnType, IReadOnlyList<string> ParameterTypes);

    /// <summary>Each method <paramref name="assemblyFile"/> calls, makes or takes the address of, with the method that does.</summary>
    public static IReadOnlyList<(string Caller, CalledMethod Called)> CallsIn(string assemblyFile)
    {
        using var stream = File.OpenRead(assemblyFile);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var calls = new List<(string, CalledMethod)>();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            var typeName = NameOf(metadata, type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var caller = $"{typeName}.{metadata.GetString(method.Name)}";
                var il = image.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    short value = il.ReadByte();
                    if (value == 0xFE)
                    {
                        value = unchecked((short)(0xFE00 | il.ReadByte()));
                    }

                    var opCode = IlReader.OpCodesByValue[value];
                    if (opCode.OperandType == OperandType.InlineMethod)
                    {
                        calls.Add((caller, Called(metadata, MetadataTokens.EntityHandle(il.ReadInt32()))));
                        continue;
                    }

                    // A switch's targets follow their count, which is read first: the offset moves past it as it's read.
                    var operandSize = opCode.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 * il.ReadInt32(),
                        _ => 4,
                    };
                    il.Offset += operandSize;
                }
            }
        }

        return calls;
    }

    private static CalledMethod Called(MetadataReader metadata, EntityHandle handle, ImmutableArray<string> typeArguments = default)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var definition = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return Of(metadata.GetString(definition.Name), definition.DecodeSignature(SignatureNames.Instance, typeArguments));
            case HandleKind.MemberReference:
                var reference = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return Of(metadata.GetString(reference.Name), reference.DecodeMethodSignature(SignatureNames.Instance, typeArguments));
            case HandleKind.MethodSpecification:
                // A generic method's call: its own signature, with the arguments it's called with in place of !!0, !!1…
                var specification = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                return Called(metadata, specification.Method, specification.DecodeSignature(SignatureNames.Instance, default));
            default:
                throw new BadImageFormatException($"A method operand of kind {handle.Kind}.");
        }
    }

    private static CalledMethod Of(string name, MethodSignature<string> signature) =>
        new(name, signature.ReturnType, signature.ParameterTypes);

    private static string NameOf(MetadataReader metadata, TypeDefinition type)
    {
        var name = metadata.GetString(type.Name);
        return type.GetDeclaringType() is { IsNil: false } outer
            ? $"{NameOf(metadata, metadata.GetTypeDefinition(outer))}+{name}"
            : $"{metadata.GetString(type.Namespace)}.{name}";
    }

    /// <summary>A signature's types by their full names, a generic method's parameters by the arguments it's called with.</summary>
    private sealed class SignatureNames : ISignatureTypeProvider<string, ImmutableArray<string>>
    {
        public static readonly SignatureNames Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => "System." + typeCode,
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            NameOf(reader, reader.GetTypeDefinition(handle));

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? $"{GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)}+{name}"
                : $"{reader.GetString(type.Namespace)}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, ImmutableArray<string> genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericMethodParameter(ImmutableArray<string> genericContext, int index) =>
            !genericContext.IsDefault && index < genericContext.Length ? genericContext[index] : $"!!{index}";

        public string GetGenericTypeParameter(ImmutableArray<string> genericContext, int index) => $"!{index}";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";


    }
}
