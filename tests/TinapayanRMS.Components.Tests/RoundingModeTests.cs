using System.Reflection;
using System.Reflection.Emit;
using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.Products;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// Every rounding below the screens names its midpoint mode. Math.Round and decimal.Round take a half to the even digit
/// unless told otherwise, where a number shown (N2) and a cost stored (RawMaterialRules.StoredCost) go away from zero, so a
/// call that leaves the mode out is a choice nobody made: one compared with what's shown says AwayFromZero, and one that
/// feeds a typed or saved figure says ToEven, as it always rounded (PLAN.md, Conventions, "Decimal precision"). Read from
/// each method's IL, lambdas and local functions included.
/// </summary>
public class RoundingModeTests
{
    private static readonly Assembly[] Checked =
    [
        typeof(Product).Assembly,                                  // Domain
        typeof(ApplicationCore.Rules.CostingRules).Assembly,       // ApplicationCore
        typeof(TinapayanRMSDbContext).Assembly,                    // Infrastructure
        typeof(Pricing.SavedProductReader).Assembly,               // Pricing
        typeof(VersionTrendPoint).Assembly,                        // Presentation
    ];

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    [Test]
    public void Every_rounding_below_the_screens_names_its_midpoint_mode()
    {
        var unnamed = Checked
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            .Where(RoundsWithoutAMode)
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        unnamed.Should().BeEmpty("each rounding says whether a half goes to the even digit or away from zero");
    }

    [Test]
    public void A_rounding_without_a_mode_is_found()
    {
        RoundsWithoutAMode(typeof(RoundingModeTests).GetMethod(nameof(RoundedWithoutAMode), Declared)!).Should().BeTrue();
        RoundsWithoutAMode(typeof(RoundingModeTests).GetMethod(nameof(RoundedAwayFromZero), Declared)!).Should().BeFalse();
    }

    private static decimal RoundedWithoutAMode(decimal amount) => Math.Round(amount, 2);

    private static decimal RoundedAwayFromZero(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static bool RoundsWithoutAMode(MethodBase method) =>
        CalledBy(method).Any(called =>
            (called.DeclaringType == typeof(Math) || called.DeclaringType == typeof(decimal))
            && called.Name == nameof(Math.Round)
            && called.GetParameters().All(parameter => parameter.ParameterType != typeof(MidpointRounding)));

    /// <summary>Every method <paramref name="method"/>'s IL calls (call, callvirt, newobj, ldftn), each operand read by its opcode.</summary>
    private static IEnumerable<MethodBase> CalledBy(MethodBase method)
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
            if (opCode.OperandType == OperandType.InlineMethod && Resolve(method.Module, BitConverter.ToInt32(il, at), typeArguments, methodArguments) is { } called)
            {
                yield return called;
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

    private static MethodBase? Resolve(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
        {
            return null;
        }
    }
}
