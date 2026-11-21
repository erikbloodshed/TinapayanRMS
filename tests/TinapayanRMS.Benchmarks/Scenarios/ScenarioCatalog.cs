using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.UseCases.PriceSimulator;
using TinapayanRMS.UseCases.Products;
using TinapayanRMS.UseCases.RawMaterials;
using TinapayanRMS.UseCases.Reports;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// Every scenario, by name: what BenchmarkDotNet times (Suites) and the budgets count (Benchmarks.Tests), so a scenario
/// added here is in both. A name is what the comparison and the budgets file know it by: renaming one starts its history
/// again.
/// </summary>
public static class ScenarioCatalog
{
    private static readonly IReadOnlyDictionary<string, Func<Scenario>> ByName = new Dictionary<string, Func<Scenario>>
    {
        ["Pricing.ReadAll"] = () => new ReadScenario<IReadOnlyList<SavedProduct>>(
            "Pricing.ReadAll",
            async services =>
            {
                var products = await services.GetRequiredService<IProductRepository>().GetAllAsync(CancellationToken.None);
                var reader = services.GetRequiredService<SavedProductReader>();
                return () => reader.ReadAllAsync(products, CostBasis.Buffered, CancellationToken.None);
            },
            read => read.Count == 0 ? "no product was priced" : null),

        ["Products.ReadLoad"] = () => new ReadScenario<ProductsLoad>(
            "Products.ReadLoad",
            services =>
            {
                var queries = services.GetRequiredService<IProductQueries>();
                return Task.FromResult<Func<Task<ProductsLoad>>>(() => queries.ReadLoadAsync([], [], [], CancellationToken.None));
            },
            load => load.Products.Count == 0 ? "no product was read" : null),

        ["Products.ReadDetail"] = () => new ReadScenario<ProductDetail>(
            "Products.ReadDetail",
            async services =>
            {
                var heaviest = (await FixtureRecords.ProductsByWeightAsync(services))[0];
                var queries = services.GetRequiredService<IProductQueries>();
                return () => queries.ReadDetailAsync(heaviest.Id, CancellationToken.None);
            },
            detail => detail.SubAssemblyLines.Count + detail.Components.Count == 0 ? "the product read has no recipe" : null),

        ["RawMaterials.ReadCatalog"] = () => new ReadScenario<RawMaterialsCatalog>(
            "RawMaterials.ReadCatalog",
            services =>
            {
                var queries = services.GetRequiredService<IRawMaterialQueries>();
                return Task.FromResult<Func<Task<RawMaterialsCatalog>>>(() => queries.ReadCatalogAsync([], CancellationToken.None));
            },
            catalog => catalog.RawMaterials.Count == 0 ? "no raw material was read" : null),

        ["SubAssemblies.ReadCatalog"] = () => new ReadScenario<SubAssembliesCatalog>(
            "SubAssemblies.ReadCatalog",
            services =>
            {
                var queries = services.GetRequiredService<ISubAssemblyQueries>();
                return Task.FromResult<Func<Task<SubAssembliesCatalog>>>(() => queries.ReadCatalogAsync(CancellationToken.None));
            },
            catalog => catalog.SubAssemblies.Count == 0 ? "no sub-assembly was read" : null),

        ["Reports.Read"] = () => new ReadScenario<ReportsRead>(
            "Reports.Read",
            services =>
            {
                var queries = services.GetRequiredService<IReportQueries>();
                return Task.FromResult<Func<Task<ReportsRead>>>(() => queries.ReadAsync(CancellationToken.None));
            },
            read => read.Products.Count == 0 || read.Details.Count < read.Products.Count ? "not every product was read" : null),

        ["PriceSimulator.Read"] = () => new ReadScenario<PriceSimulatorRead>(
            "PriceSimulator.Read",
            services =>
            {
                var queries = services.GetRequiredService<IPriceSimulatorQueries>();
                return Task.FromResult<Func<Task<PriceSimulatorRead>>>(() => queries.ReadAsync(CancellationToken.None));
            },
            read => read.Products.Count == 0 || read.Details.Count < read.Products.Count ? "not every product was read" : null),

        ["PriceSimulator.Simulate"] = () => new SimulationScenario(),
        ["Cascade.SaveFlourPrice"] = () => new CascadeScenario(),
        ["Screens.OpenProduct"] = ScreenScenarios.OpenProduct,
        ["Screens.PickProduct"] = () => new PickProductScenario(),
        ["Screens.OpenRawMaterials"] = ScreenScenarios.OpenRawMaterials,
        ["Screens.OpenSubAssemblies"] = ScreenScenarios.OpenSubAssemblies,
        ["Start.SignInAndFirstScreen"] = () => new StartScenario(),
    };

    /// <summary>Every scenario's name, in the order above.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. ByName.Keys];

    /// <summary>A new scenario named <paramref name="name"/>, not set up.</summary>
    public static Scenario Create(string name) =>
        ByName.TryGetValue(name, out var create)
            ? create()
            : throw new ArgumentException($"There's no scenario named {name}: there are {string.Join(", ", Names)}.", nameof(name));

    /// <summary>
    /// The names <paramref name="patterns"/> match, in the catalog's order: one or more patterns, a comma between them, where
    /// * stands for anything (Screens.*,Pricing.ReadAll); every name for none.
    /// </summary>
    public static IReadOnlyList<string> Matching(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns) || patterns == "*")
        {
            return Names;
        }

        Regex[] matches =
        [
            .. patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(pattern => new Regex(
                "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ];
        return [.. Names.Where(name => matches.Any(regex => regex.IsMatch(name)))];
    }
}
