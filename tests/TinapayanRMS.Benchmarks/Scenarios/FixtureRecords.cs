using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The records the scenarios pick, picked the same way on every run of the same data, so two builds time the same work:
/// the products with the most lines, and the golden master's materials by name.
/// </summary>
public static class FixtureRecords
{
    /// <summary>The golden master's four price changes: flour on most products, the egg two linked ingredients are cut from, a milk made ingredients use, and packaging.</summary>
    public static readonly IReadOnlyList<(string Material, decimal ChangePercent)> PriceChanges =
    [
        ("Material 11", 10m),
        ("Material 45", 15m),
        ("Material 42", -5m),
        ("Material 75", 20m),
    ];

    /// <summary>The material a price save of reaches the most products: the golden master's flour.</summary>
    public const string Flour = "Material 11";

    /// <summary>Every product, the one with the most lines (recipe, add-in, packaging and composite) first, then by id.</summary>
    public static async Task<IReadOnlyList<Product>> ProductsByWeightAsync(IServiceProvider services)
    {
        var products = await services.GetRequiredService<IProductRepository>().GetAllAsync(CancellationToken.None);
        var details = await services.GetRequiredService<SavedProductReader>().ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
        return [.. products.OrderByDescending(p => LinesOf(details[p.Id])).ThenBy(p => p.Id)];
    }

    private static int LinesOf(ProductDetail detail) =>
        detail.SubAssemblyLines.Count + detail.AddIns.Count + detail.Lines.Count + detail.BulkLines.Count + detail.Components.Count;

    /// <summary>The raw material named <paramref name="name"/>.</summary>
    public static async Task<RawMaterial> MaterialAsync(IServiceProvider services, string name) =>
        (await services.GetRequiredService<IRawMaterialRepository>().GetAllAsync(CancellationToken.None)).SingleOrDefault(m => m.Name == name)
        ?? throw new InvalidOperationException($"The fixture has no raw material named {name}.");
}
