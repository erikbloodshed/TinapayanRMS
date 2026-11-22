using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.Products;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// Every amount, quantity and percentage is a decimal: a double can't hold 0.1 or 28.35 exactly, and since .NET 11 a
/// double turned into a decimal keeps its binary noise (28.35 becomes 28.3499…). So no field, property, parameter,
/// return value or local variable of the app's own code below the screens is a double or a float, however it's wrapped
/// (double?, double[], IReadOnlyList&lt;double&gt;), except where a number leaves for something that only takes doubles.
/// And none of it, the screens included, turns one into a decimal: a cast is a built-in conversion BannedSymbols.txt
/// can't name, so it's read from each method's IL.
/// </summary>
public partial class DecimalNumbersTests
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
        Codebase.UseCases,                                         // UseCases
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

    [Test]
    public void No_double_or_float_is_turned_into_a_decimal()
    {
        var converting = Checked.Append(Codebase.Components)
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(IlReader.Bodies)
            .Where(TurnsBinaryIntoDecimal)
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Concat(UnreferencedAssemblies.Files.Values.SelectMany(ConvertingIn))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, converting).Should().BeEmpty("since .NET 11 the decimal keeps the double's binary noise: take it from text or a literal");
    }

    [Test]
    public void The_app_and_the_two_servers_are_read_from_their_builds()
    {
        UnreferencedAssemblies.Files.Keys.Should().BeEquivalentTo("TinapayanRMS.Maui", "TinapayanRMS.RecipeApi", "TinapayanRMS.Mcp");
        UnreferencedAssemblies.Files.Values.Should().OnlyContain(file => File.Exists(file));
    }

    [Test]
    public void A_double_or_float_turned_into_a_decimal_is_found_in_an_assembly_read_from_its_file()
    {
        var converting = ConvertingIn(typeof(DecimalNumbersTests).Assembly.Location)
            .Where(method => method.StartsWith(typeof(DecimalNumbersTests).FullName + ".", StringComparison.Ordinal))
            .Select(method => method[(typeof(DecimalNumbersTests).FullName!.Length + 1)..]);

        converting.Should().BeEquivalentTo(
            nameof(CastFromADouble), nameof(CastFromANullableDouble), nameof(CastFromAFloat), nameof(CastFromAHalf), nameof(CreatedFromADouble));
    }

    /// <summary>The methods of the assembly in <paramref name="assemblyFile"/> that turn a double, a float or a Half into a decimal.</summary>
    private static IEnumerable<string> ConvertingIn(string assemblyFile) =>
        UnreferencedAssemblies.CallsIn(assemblyFile)
            .Where(call => call.Called.ReturnType == "System.Decimal"
                && (call.Called.Name is "op_Explicit" or "op_Implicit" || call.Called.Name.StartsWith("Create", StringComparison.Ordinal))
                && call.Called.ParameterTypes.Any(parameter => BinaryName().IsMatch(parameter)))
            .Select(call => call.Caller)
            .Distinct();

    /// <summary>A double, a float or a Half, alone or inside a type it's built from (System.Nullable`1&lt;System.Double&gt;).</summary>
    [GeneratedRegex(@"\bSystem\.(Double|Single|Half)\b")]
    private static partial Regex BinaryName();

    [Test]
    [Arguments(nameof(CastFromADouble))]
    [Arguments(nameof(CastFromANullableDouble))]
    [Arguments(nameof(CastFromAFloat))]
    [Arguments(nameof(CastFromAHalf))]
    [Arguments(nameof(CreatedFromADouble))]
    public void A_double_or_float_turned_into_a_decimal_is_found(string method) =>
        TurnsBinaryIntoDecimal(typeof(DecimalNumbersTests).GetMethod(method, Declared)!).Should().BeTrue();

    [Test]
    public void A_decimal_from_text_or_a_whole_number_is_let_through() =>
        TurnsBinaryIntoDecimal(typeof(DecimalNumbersTests).GetMethod(nameof(FromTextAndAWholeNumber), Declared)!).Should().BeFalse();

    private static decimal CastFromADouble(double number) => (decimal)number;

    private static decimal? CastFromANullableDouble(double? number) => (decimal?)number;

    private static decimal CastFromAFloat(float number) => (decimal)number;

    private static decimal CastFromAHalf(Half number) => (decimal)number;

    private static decimal CreatedFromADouble(double number) => decimal.CreateChecked(number);

    private static decimal FromTextAndAWholeNumber(string text, int whole) =>
        decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) + whole;

    /// <summary>A conversion to decimal from a double, a float or a Half: a cast's operator, or decimal.CreateChecked and its kin.</summary>
    private static bool TurnsBinaryIntoDecimal(MethodBase method) =>
        IlReader.UsedBy(method).OfType<MethodInfo>().Any(called =>
            called.ReturnType == typeof(decimal)
            && (called.Name is "op_Explicit" or "op_Implicit" || called.Name.StartsWith("Create", StringComparison.Ordinal))
            && called.GetParameters().Any(parameter => IsBinary(parameter.ParameterType)));

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
