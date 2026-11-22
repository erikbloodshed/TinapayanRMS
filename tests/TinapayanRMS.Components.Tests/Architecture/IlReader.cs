using System.Reflection;
using System.Reflection.Emit;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// What a method's IL names, for the architecture tests that read method bodies: each method it calls, makes or takes the
/// address of, each type it casts to or asks typeof of, and each field it reads or writes.
/// </summary>
internal static class IlReader
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    /// <summary>Every method and constructor <paramref name="type"/> declares, its static constructor included.</summary>
    public static IEnumerable<MethodBase> Bodies(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));

    /// <summary>What <paramref name="method"/>'s IL names, each operand read by its opcode.</summary>
    public static IEnumerable<MemberInfo> UsedBy(MethodBase method)
    {
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            yield break;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;
        for (var at = 0; at < il.Length;)
        {
            short value = il[at++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[at++]));
            }

            var opCode = OpCodesByValue[value];
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok or OperandType.InlineField
                && method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments) is { } used)
            {
                yield return used;
            }

            at += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }
    }
}
