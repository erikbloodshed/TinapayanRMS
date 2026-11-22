using System.Reflection;
using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.Products;

namespace TinapayanRMS.Components.Tests.Architecture;

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
        Codebase.UseCases,                                         // UseCases
        typeof(VersionTrendPoint).Assembly,                        // Presentation
    ];

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

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
        IlReader.UsedBy(method).OfType<MethodBase>().Any(called =>
            (called.DeclaringType == typeof(Math) || called.DeclaringType == typeof(decimal))
            && called.Name == nameof(Math.Round)
            && called.GetParameters().All(parameter => parameter.ParameterType != typeof(MidpointRounding)));
}
