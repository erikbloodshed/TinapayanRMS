using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// The screens reach the database through use cases (docs/adr/0002-layers-use-cases-and-drafts.md), never by holding a
/// repository, the unit of work, the DbContext or a service built on them. The screens' types that still held one on
/// 2026-11-20 are listed with how many they held: a count may only go down, and is lowered here when it does, so the list
/// only shrinks as each screen moves to use cases. A type not listed may hold none.
/// </summary>
public class DataAccessTests
{
    /// <summary>34 types holding 121 between them on 2026-11-20.</summary>
    private static readonly Dictionary<string, int> StillReaching = new(StringComparer.Ordinal)
    {
        ["TinapayanRMS.Presentation.App.AutomaticBackups"] = 1,
        ["TinapayanRMS.Presentation.App.DatabaseReadiness"] = 1,
        ["TinapayanRMS.Presentation.App.QuickFindState"] = 3,
        ["TinapayanRMS.Presentation.App.SignInState"] = 1,
        ["TinapayanRMS.Presentation.PriceSimulator.PriceSimulatorState"] = 6,
        ["TinapayanRMS.Presentation.Printing.PrintDialogState"] = 4,
        ["TinapayanRMS.Presentation.Printing.PrintPage"] = 1,
        ["TinapayanRMS.Presentation.Products.ProductComponents"] = 1,
        ["TinapayanRMS.Presentation.Products.ProductForm"] = 9,
        ["TinapayanRMS.Presentation.Products.ProductPackaging"] = 2,
        ["TinapayanRMS.Presentation.Products.ProductRecipe"] = 2,
        ["TinapayanRMS.Presentation.Products.ProductsList"] = 2,
        ["TinapayanRMS.Presentation.Products.ProductsLoadReader"] = 11,
        ["TinapayanRMS.Presentation.Products.ProductsState"] = 18,
        ["TinapayanRMS.Presentation.Products.ProductVersionHistory"] = 6,
        ["TinapayanRMS.Presentation.RawMaterials.MaterialCostShares"] = 4,
        ["TinapayanRMS.Presentation.RawMaterials.RawMaterialForm"] = 6,
        ["TinapayanRMS.Presentation.RawMaterials.RawMaterialPriceLog"] = 1,
        ["TinapayanRMS.Presentation.RawMaterials.RawMaterialRecipe"] = 1,
        ["TinapayanRMS.Presentation.RawMaterials.RawMaterialsList"] = 2,
        ["TinapayanRMS.Presentation.RawMaterials.RawMaterialsState"] = 9,
        ["TinapayanRMS.Presentation.Reports.ReportsState"] = 6,
        ["TinapayanRMS.Presentation.Settings.CostingDefaultsState"] = 2,
        ["TinapayanRMS.Presentation.Settings.DatabaseBackupsState"] = 1,
        ["TinapayanRMS.Presentation.Settings.PrintingState"] = 1,
        ["TinapayanRMS.Presentation.Settings.ProductCategoriesState"] = 1,
        ["TinapayanRMS.Presentation.Settings.RawMaterialCategoriesState"] = 1,
        ["TinapayanRMS.Presentation.Settings.SecurityState"] = 1,
        ["TinapayanRMS.Presentation.Settings.SubAssemblyRolesState"] = 1,
        ["TinapayanRMS.Presentation.Settings.UnitsOfMeasureState"] = 1,
        ["TinapayanRMS.Presentation.SubAssemblies.SharedRecipeImpact"] = 3,
        ["TinapayanRMS.Presentation.SubAssemblies.SubAssembliesList"] = 3,
        ["TinapayanRMS.Presentation.SubAssemblies.SubAssembliesState"] = 6,
        ["TinapayanRMS.Presentation.SubAssemblies.SubAssemblyForm"] = 3,
    };

    /// <summary>
    /// Ports Infrastructure implements that aren't the database: the window's view state is a file of the user's. Before
    /// <see cref="DataAccess"/>, which reads it as it's made.
    /// </summary>
    private static readonly HashSet<Type> NotTheDatabase = [typeof(ApplicationCore.Interfaces.IViewStateStore)];

    [Test]
    public void The_screens_reach_the_database_only_where_they_still_did_on_2026_11_20()
    {
        var reaching = Codebase.WrittenTypes(Codebase.Presentation).Concat(Codebase.WrittenTypes(Codebase.Components))
            .Select(type => (Name: type.FullName!, Holds: DataAccessHeldBy(type).Order(StringComparer.Ordinal).ToList()))
            .Where(type => type.Holds.Count > 0)
            .ToDictionary(type => type.Name, type => type.Holds, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (name, holds) in reaching.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var listed = StillReaching.GetValueOrDefault(name);
            if (holds.Count > listed)
            {
                problems.Add($"{name} holds {holds.Count} data-access types where {listed} are allowed ({string.Join(", ", holds)}): reach the database through a use case (ADR 0002)");
            }
            else if (holds.Count < listed)
            {
                problems.Add($"{name} holds {holds.Count} data-access types now: lower StillReaching to {holds.Count}");
            }
        }

        problems.AddRange(StillReaching.Keys
            .Where(name => !reaching.ContainsKey(name))
            .Select(name => $"{name} holds no data-access type now: take it out of StillReaching"));

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }

    /// <summary>The data-access types <paramref name="type"/> holds: in a field, or taken by its constructor or an [Inject] property.</summary>
    private static IEnumerable<string> DataAccessHeldBy(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var held = type.GetFields(declared).Select(field => field.FieldType)
            .Concat(type.GetConstructors(declared).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            .Concat(type.GetProperties(declared).Where(property => property.IsDefined(typeof(InjectAttribute))).Select(property => property.PropertyType));

        return held.SelectMany(Codebase.Unwrapped).Where(DataAccess.Contains).Select(found => found.Name).Distinct();
    }

    /// <summary>
    /// What reaches the database: the DbContext and anything of Infrastructure's or EF Core's; the ports Infrastructure
    /// implements (the repositories, the unit of work, the cloner, the backups); and a service below the screens built on
    /// any of those (Pricing's SavedProductReader and VersionedSaves).
    /// </summary>
    private static readonly HashSet<Type> DataAccess = FindDataAccess();

    private static HashSet<Type> FindDataAccess()
    {
        var implemented = Codebase.Infrastructure.GetTypes().SelectMany(type => type.GetInterfaces()).ToHashSet();
        var found = Codebase.ApplicationCore.GetTypes()
            .Where(type => type.IsInterface && implemented.Contains(type) && !NotTheDatabase.Contains(type))
            .Concat(Codebase.WrittenTypes(Codebase.Infrastructure).Where(type => type.IsPublic || type.IsNestedPublic))
            .ToHashSet();

        // Services built on those, as far as they go: a reader that takes a repository, a save that takes that reader.
        var services = Codebase.WrittenTypes(Codebase.Pricing).Concat(Codebase.WrittenTypes(Codebase.ApplicationCore))
            .Where(type => type.IsClass && !type.IsAbstract)
            .ToList();
        bool grew;
        do
        {
            grew = false;
            foreach (var service in services.Where(service => !found.Contains(service)))
            {
                if (service.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                    .SelectMany(parameter => Codebase.Unwrapped(parameter.ParameterType))
                    .Any(type => found.Contains(type) || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true))
                {
                    grew |= found.Add(service);
                }
            }
        }
        while (grew);

        return found;
    }
}
