using System.Collections.Immutable;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>Everything a product's detail shows, as it's saved: what <see cref="SavedProductReader.ReadDetailAsync"/> reads.</summary>
public sealed record ProductDetail(
    PackagingLink? PackagingLink,
    IReadOnlyList<ProductSubAssemblyUsage> Usages,
    IReadOnlyList<PackagingLine> Lines,
    CostingParameters? Costing,
    IReadOnlyList<SubAssemblyLine> SubAssemblyLines,
    IReadOnlyList<UsageAddInLine> AddIns)
{
    /// <summary>A composite's products, in its Products table's order, each with its recipe cost as saved; none for a product made of sub-assemblies.</summary>
    public IReadOnlyList<SavedComponent> Components { get; init; } = [];
}

/// <summary>A product's recipe cost for one sellable unit at each basis: its ingredients at their buffered costs, and at their true ones.</summary>
public sealed record RecipeCosts(decimal Buffered, decimal Unbuffered)
{
    public decimal At(CostBasis basis) => basis == CostBasis.Unbuffered ? Unbuffered : Buffered;
}

/// <summary>One product in a saved composite, with what one of it brings: its recipe cost for one sellable unit, as saved.</summary>
public sealed record SavedComponent(ProductComponent Component, RecipeCosts RecipeCosts)
{
    /// <summary>What one of the product brings at <paramref name="basis"/>.</summary>
    public decimal RecipeCostAt(CostBasis basis) => RecipeCosts.At(basis);

    /// <summary>What the line brings into the composite's cost at <paramref name="basis"/>.</summary>
    public decimal CostAt(CostBasis basis) => CompositeProductRules.CalculateComponentCost(RecipeCostAt(basis), Component.Quantity);

    /// <summary>"Ensaymada × 6", what the Costing tab calls the line.</summary>
    public string LineName => ProductComponentRow.LineNameFor(Component.ComponentProduct!.Name, Component.Quantity);
}

/// <summary>
/// A saved product as the Products screen shows it once opened: its sections in recipe order, its packaging, and its
/// costing worked out to the selling price. What a set of products prints, and what Reports prices.
/// </summary>
/// <param name="PackagingLinkedTo">The Packaging tab's "Linked to …" banner, or null for the product's own lines.</param>
/// <param name="CostBasis">Which of its materials' costs it's priced at: buffered, unless the Products screen is switched to unbuffered.</param>
public sealed record SavedProduct(
    Product Product,
    IReadOnlyList<ProductUsageEdit> Sections,
    IReadOnlyList<PackagingLineRow> PackagingLines,
    bool HasPackaging,
    string? PackagingLinkedTo,
    ProductCostSummary Costs,
    CostBasis CostBasis = CostBasis.Buffered)
{
    /// <summary>A composite's products, each with its recipe cost; none for a product made of sub-assemblies.</summary>
    public IReadOnlyList<SavedComponent> Components { get; init; } = [];

    /// <summary>What Print puts on paper for it: as saved, so with no save note.</summary>
    public ProductPrintout ToPrintout(DateTime printedAt) => new(
        Product.Name,
        Product.Category?.Name ?? ProductFolderNode.UncategorizedTitle,
        ProductFolderNode.StageTitle(Product.Stage),
        ProductPrintout.CostUnitCaptionFor(Product.QuantityRequired, Product.IsComposite),
        ProductPrintout.LastUpdatedTextFor(Product.UpdatedAt),
        null,
        printedAt,
        [.. Sections.Select(s => PrintedSection.From(s, CostBasis))],
        HasPackaging ? PrintedPackaging.From(PackagingLines, PackagingLinkedTo) : null,
        Costs,
        Product.IsComposite
            ? [.. Components.Select(c => new PrintedComponent(c.Component.ComponentProduct!.Name, c.Component.Quantity, c.RecipeCostAt(CostBasis), c.CostAt(CostBasis)))]
            : null)
    {
        CostBasis = CostBasis,
    };
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
    ISubAssemblyLineRepository subAssemblyLineRepository,
    IProductRepository productRepository)
{
    /// <summary>
    /// The product's saved detail: its packaging link and lines, its recipe with its sections' lines and add-ins, a
    /// composite's products with their recipe costs, and its costing.
    /// </summary>
    public Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken) =>
        ReadDetailAsync(productId, [], cancellationToken);

    /// <param name="holders">The composites this read is inside, each holding the next: none may come round again.</param>
    private async Task<ProductDetail> ReadDetailAsync(int productId, ImmutableHashSet<int> holders, CancellationToken cancellationToken)
    {
        // Save refuses a composite that would hold itself, so this only guards against data changed outside the app,
        // which would otherwise read round the loop for ever.
        if (holders.Contains(productId))
        {
            throw new InvalidOperationException("A composite product holds itself, through the products in it. Take it out of one of them.");
        }

        // At once, each on its own context: a read is mostly a round trip's wait. Only the sections' lines wait, for
        // the sections, and a composite's products' costs for its lines. Reports reads several products this way at a
        // time (ProductsReadAtOnce).
        var packagingLinkRead = packagingLinkRepository.GetForProductAsync(productId, cancellationToken);
        var linesRead = packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
        var costingRead = costingParametersRepository.GetForProductAsync(productId, cancellationToken);
        var addInsRead = usageRepository.GetAddInsForProductAsync(productId, cancellationToken);
        var usagesRead = usageRepository.GetForProductAsync(productId, cancellationToken);
        var subAssemblyLinesRead = ReadSectionLinesAsync(usagesRead, cancellationToken);
        var componentsRead = ReadComponentsAsync(productId, holders.Add(productId), cancellationToken);
        await Task.WhenAll(packagingLinkRead, linesRead, costingRead, addInsRead, usagesRead, subAssemblyLinesRead, componentsRead);

        return new ProductDetail(
            await packagingLinkRead, await usagesRead, await linesRead, await costingRead, await subAssemblyLinesRead, await addInsRead)
        {
            Components = await componentsRead,
        };
    }

    private async Task<IReadOnlyList<SubAssemblyLine>> ReadSectionLinesAsync(
        Task<IReadOnlyList<ProductSubAssemblyUsage>> usagesRead, CancellationToken cancellationToken) =>
        await subAssemblyLineRepository.GetForSubAssembliesAsync(
            [.. (await usagesRead).Select(u => u.SubAssemblyId).Distinct()], cancellationToken);

    /// <summary>A composite's lines, each with its product's recipe cost as saved, read at once; none for any other product.</summary>
    private async Task<IReadOnlyList<SavedComponent>> ReadComponentsAsync(
        int productId, ImmutableHashSet<int> holders, CancellationToken cancellationToken)
    {
        var components = await productRepository.GetComponentsAsync(productId, cancellationToken);
        if (components.Count == 0)
        {
            return [];
        }

        var costs = await Task.WhenAll(components
            .Select(c => c.ComponentProduct!)
            .DistinctBy(p => p.Id)
            .Select(async p => (p.Id, Cost: await ReadRecipeCostAsync(p, holders, cancellationToken))));
        var costById = costs.ToDictionary(c => c.Id, c => c.Cost);
        return [.. components.Select(c => new SavedComponent(c, costById[c.ComponentProductId]))];
    }

    /// <summary>
    /// What one of each product brings into a composite, as it's saved: its recipe (sub-assemblies, ingredients) cost for
    /// one sellable unit, which is the Sub-assemblies figure on its Costing tab, buffered and unbuffered. A product that's
    /// gone is left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, RecipeCosts>> ReadRecipeCostsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, RecipeCosts>();
        }

        // Read again rather than taken from the screen's list, which may be older than a quantity required saved since.
        var products = await productRepository.GetByIdsAsync(productIds, cancellationToken);
        var costs = await Task.WhenAll(products.Select(async p => (p.Id, Cost: await ReadRecipeCostAsync(p, [], cancellationToken))));
        return costs.ToDictionary(c => c.Id, c => c.Cost);
    }

    /// <summary>Its recipe cost at both bases, from one read: pricing it twice is only arithmetic.</summary>
    private async Task<RecipeCosts> ReadRecipeCostAsync(Product product, ImmutableHashSet<int> holders, CancellationToken cancellationToken)
    {
        var detail = await ReadDetailAsync(product.Id, holders, cancellationToken);
        return new(
            Build(product, detail, CostBasis.Buffered).Costs.SubAssemblies,
            Build(product, detail, CostBasis.Unbuffered).Costs.SubAssemblies);
    }

    /// <summary><paramref name="product"/> as it's saved, read and priced at its buffered costs.</summary>
    public Task<SavedProduct> ReadAsync(Product product, CancellationToken cancellationToken) =>
        ReadAsync(product, CostBasis.Buffered, cancellationToken);

    /// <summary><paramref name="product"/> as it's saved, read and priced at <paramref name="basis"/>.</summary>
    public async Task<SavedProduct> ReadAsync(Product product, CostBasis basis, CancellationToken cancellationToken) =>
        Build(product, await ReadDetailAsync(product.Id, cancellationToken), basis);

    /// <summary>
    /// <paramref name="detail"/> as the screen shows it: each section with the ingredients and role it was read with,
    /// and the costing from the saved costing, the product's quantity per unit and its current price, as RecomputeCosts
    /// works it out from the form. A composite's recipe cost is its products', then its own sections'.
    /// </summary>
    public static SavedProduct Build(Product product, ProductDetail detail) => Build(product, detail, CostBasis.Buffered);

    /// <summary>The same, priced at <paramref name="basis"/>: its materials' buffered costs, or their true ones.</summary>
    public static SavedProduct Build(Product product, ProductDetail detail, CostBasis basis)
    {
        var linesBySubAssembly = detail.SubAssemblyLines.ToLookup(l => l.SubAssemblyId);
        var addInsByUsage = detail.AddIns.ToLookup(a => a.UsageId);
        List<ProductUsageEdit> sections =
        [
            .. detail.Usages
                .Select(u => ProductUsageEdit.FromSaved(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], u.SubAssembly!.Role, product.Name))
                .Order(ProductUsageEdit.RecipeOrder),
        ];
        List<PackagingLineRow> packagingLines = [.. detail.Lines.Select(l => PackagingLineRow.FromSaved(l, basis))];

        var costing = detail.Costing;
        var costs = new ProductCostSummary(
            [
                .. detail.Components.Select(c => (c.LineName, c.CostAt(basis))),
                .. sections.Select(u => (u.SectionName, u.CostPerPieceAt(basis))),
            ],
            product.QuantityRequired,
            [.. packagingLines.Select(l => l.CostPerUnit)],
            costing?.LaborPercent ?? 0m,
            costing?.OverheadPercent ?? 0m,
            costing?.AdministrativePercent ?? 0m,
            costing?.MarginPercent ?? 0m,
            costing?.VatPercent ?? 0m,
            product.CurrentPrice,
            product.IsComposite,
            product.IsComposite && sections.Count > 0);

        return new SavedProduct(
            product,
            sections,
            packagingLines,
            detail.PackagingLink is not null || detail.Lines.Count > 0,
            detail.PackagingLink is { } link ? PackagingLinkTarget.From(link).LinkedToText : null,
            costs,
            basis)
        {
            Components = detail.Components,
        };
    }
}
