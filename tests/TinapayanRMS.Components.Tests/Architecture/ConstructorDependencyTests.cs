using System.Reflection;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// A screen's state, a part of one or a row takes at most <see cref="Limit"/> things through its constructor
/// (docs/adr/0004-quality-gates.md): one that needs more is doing more than one job. Those that took more on 2026-11-20 are
/// listed with how many they took: a count may only go down, and is lowered here when it does, so the list only shrinks.
/// </summary>
public class ConstructorDependencyTests
{
    private const int Limit = 6;

    private static readonly Dictionary<string, int> StillOver = new(StringComparer.Ordinal)
    {
        ["TinapayanRMS.Presentation.PriceSimulator.PriceSimulatorState"] = 7,
        ["TinapayanRMS.Presentation.Printing.PrintDialogState"] = 7,
        ["TinapayanRMS.Presentation.Products.ProductsList"] = 7,
        ["TinapayanRMS.Presentation.Products.ProductUsageEdit"] = 13,
        ["TinapayanRMS.Presentation.Products.ProductVersionHistory"] = 7,
    };

    [Test]
    public void A_screen_state_takes_at_most_six_things_through_its_constructor()
    {
        const BindingFlags constructors = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var counts = Codebase.WrittenTypes(Codebase.Presentation)
            .Where(type => type.IsClass && typeof(ObservableState).IsAssignableFrom(type))
            .Select(type => (Name: type.FullName!, Takes: type.GetConstructors(constructors).Select(c => c.GetParameters().Length).DefaultIfEmpty(0).Max()))
            .Where(type => type.Takes > Limit)
            .ToDictionary(type => type.Name, type => type.Takes, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (name, takes) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var allowed = StillOver.GetValueOrDefault(name, Limit);
            if (takes > allowed)
            {
                problems.Add($"{name} takes {takes} things through its constructor where {allowed} are allowed: split its jobs (ADR 0002)");
            }
            else if (takes < allowed)
            {
                problems.Add($"{name} takes {takes} now: lower StillOver to {takes}");
            }
        }

        problems.AddRange(StillOver.Keys
            .Where(name => !counts.ContainsKey(name))
            .Select(name => $"{name} takes {Limit} or fewer now: take it out of StillOver"));

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }
}
