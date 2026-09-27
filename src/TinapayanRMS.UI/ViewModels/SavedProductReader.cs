using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>Everything a product's detail shows, as it's saved: what <see cref="SavedProductReader.ReadDetailAsync"/> reads.</summary>
public sealed record ProductDetail(
    PackagingLink? PackagingLink,
    IReadOnlyList<ProductSubAssemblyUsage> Usages,
    IReadOnlyList<PackagingLine> Lines,
    CostingParameters? Costing,
    IReadOnlyList<SubAssemblyLine> SubAssemblyLines,
    IReadOnlyList<UsageAddInLine> AddIns);

/// <summary>
/// A saved product as the Products screen shows it once opened: its sections in recipe order, its packaging, and its
/// costing worked out to the selling price. What a set of products prints, and what Reports prices.
/// </summary>
/// <param name="PackagingLinkedTo">The Packaging tab's "Linked to …" banner, or null for the product's own lines.</param>
public sealed record SavedProduct(
    Product Product,
    IReadOnlyList<ProductUsageEdit> Sections,
    IReadOnlyList<PackagingLineRow> PackagingLines,
    bool HasPackaging,
    string? PackagingLinkedTo,
    ProductCostSummary Costs)
{
    /// <summary>What Print puts on paper for it: as saved, so with no save note.</summary>
    public ProductPrintout ToPrintout(DateTime printedAt) => new(
        Product.Name,
        Product.Category?.Name ?? ProductFolderNode.UncategorizedTitle,
        ProductFolderNode.StageTitle(Product.Stage),
        ProductPrintout.CostUnitCaptionFor(Product.QuantityRequired),
        ProductPrintout.LastUpdatedTextFor(Product.UpdatedAt),
        null,
        printedAt,
        [.. Sections.Select(PrintedSection.From)],
        HasPackaging ? PrintedPackaging.From(PackagingLines, PackagingLinkedTo) : null,
        Costs);
}

/// <summary>
/// Reads a product as it's saved, and turns it into what the Products screen shows: the one place a saved product is
/// read and priced. The Products screen reads the product it opens through it, Reports prices every product with it,
/// and Print reads each product of a set with it, so all three agree. Repositories only: a read runs off the UI thread.
/// </summary>
public sealed class SavedProductReader(
    IPackagingLinkRepository packagingLinkRepository,
    IProductSubAssemblyUsageRepository usageRepository,
    IPackagingLineRepository packagingLineRepository,
    ICostingParametersRepository costingParametersRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository)
{
    /// <summary>The product's saved detail: its packaging link and lines, its recipe with its sections' lines and add-ins, and its costing.</summary>
    public async Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken)
    {
        var packagingLink = await packagingLinkRepository.GetForProductAsync(productId, cancellationToken);
        var usages = await usageRepository.GetForProductAsync(productId, cancellationToken);
        var lines = await packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
        var costing = await costingParametersRepository.GetForProductAsync(productId, cancellationToken);
        var subAssemblyLines = await subAssemblyLineRepository.GetForSubAssembliesAsync(
            [.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);
        var addIns = await usageRepository.GetAddInsForProductAsync(productId, cancellationToken);
        return new ProductDetail(packagingLink, usages, lines, costing, subAssemblyLines, addIns);
    }

    /// <summary><paramref name="product"/> as it's saved, read and priced.</summary>
    public async Task<SavedProduct> ReadAsync(Product product, CancellationToken cancellationToken) =>
        Build(product, await ReadDetailAsync(product.Id, cancellationToken));

    /// <summary>
    /// <paramref name="detail"/> as the screen shows it: each section with the ingredients and role it was read with,
    /// and the costing from the saved costing, the product's quantity per unit and its current price, as RecomputeCosts
    /// works it out from the form.
    /// </summary>
    public static SavedProduct Build(Product product, ProductDetail detail)
    {
        var linesBySubAssembly = detail.SubAssemblyLines.ToLookup(l => l.SubAssemblyId);
        var addInsByUsage = detail.AddIns.ToLookup(a => a.UsageId);
        List<ProductUsageEdit> sections =
        [
            .. detail.Usages
                .Select(u => ProductUsageEdit.FromSaved(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], u.SubAssembly!.Role, product.Name))
                .Order(ProductUsageEdit.RecipeOrder),
        ];
        List<PackagingLineRow> packagingLines = [.. detail.Lines.Select(PackagingLineRow.FromSaved)];

        var costing = detail.Costing;
        var costs = new ProductCostSummary(
            [.. sections.Select(u => (u.SectionName, u.CostPerPiece))],
            product.QuantityRequired,
            [.. packagingLines.Select(l => l.CostPerUnit)],
            costing?.LaborPercent ?? 0m,
            costing?.OverheadPercent ?? 0m,
            costing?.AdministrativePercent ?? 0m,
            costing?.MarginPercent ?? 0m,
            costing?.VatPercent ?? 0m,
            product.CurrentPrice);

        return new SavedProduct(
            product,
            sections,
            packagingLines,
            detail.PackagingLink is not null || detail.Lines.Count > 0,
            detail.PackagingLink is { } link ? PackagingLinkTarget.From(link).LinkedToText : null,
            costs);
    }
}
