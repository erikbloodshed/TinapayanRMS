using System.Reflection;
using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// Every amount, quantity and percentage is a decimal: a double can't hold 0.1 or 28.35 exactly, and since .NET 11 a
/// double turned into a decimal keeps its binary noise (28.35 becomes 28.3499…). So no field, property, parameter,
/// return value or local variable of the app's own code below the screens is a double or a float, however it's wrapped
/// (double?, double[], IReadOnlyList&lt;double&gt;), except where a number leaves for something that only takes doubles.
/// </summary>
public class DecimalNumbersTests
{
    /// <summary>
    /// The places a double is meant: each named with why, and nothing else. The History tab's chart plots doubles and
    /// leaves a gap at NaN, so its points are doubles made from the decimals as they're drawn.
    /// </summary>
    private static readonly HashSet<Type> MeantForDoubles = [typeof(VersionTrendPoint)];

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

    [Test]
    public void No_number_below_the_screens_is_a_double_or_a_float()
    {
        var doubles = Checked
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !MeantForDoubles.Contains(type) && !MeantForDoubles.Contains(type.DeclaringType!))
            .SelectMany(Uses)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // Joined, so a failure lists every one, not only the first.
        string.Join(Environment.NewLine, doubles).Should().BeEmpty("amounts, quantities and percentages are decimals; a double is only for something that takes nothing else, named in MeantForDoubles");
    }

    /// <summary>Where <paramref name="type"/> holds, takes or gives a double or a float: "Type.Member (what)".</summary>
    private static IEnumerable<string> Uses(Type type)
    {
        foreach (var field in type.GetFields(Declared).Where(f => IsBinary(f.FieldType)))
        {
            yield return $"{type.FullName}.{field.Name} (field)";
        }

        foreach (var property in type.GetProperties(Declared).Where(p => IsBinary(p.PropertyType)))
        {
            yield return $"{type.FullName}.{property.Name} (property)";
        }

        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            if (method is MethodInfo { ReturnType: var returned } && IsBinary(returned))
            {
                yield return $"{type.FullName}.{method.Name} (returns)";
            }

            foreach (var parameter in method.GetParameters().Where(p => IsBinary(p.ParameterType)))
            {
                yield return $"{type.FullName}.{method.Name} (parameter {parameter.Name})";
            }

            if (method.GetMethodBody()?.LocalVariables.Any(local => IsBinary(local.LocalType)) == true)
            {
                yield return $"{type.FullName}.{method.Name} (a local variable)";
            }
        }
    }

    private static bool IsBinary(Type type)
    {
        if (type.HasElementType)
        {
            return IsBinary(type.GetElementType()!);
        }

        return type == typeof(double) || type == typeof(float) || type == typeof(Half)
            || (type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericArguments().Any(IsBinary));
    }
}
