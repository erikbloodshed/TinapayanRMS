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
    /// <summary>A composite's products, in its Products table's order, each with its recipe and packaging costs as saved; none for a product made of sub-assemblies.</summary>
    public IReadOnlyList<SavedComponent> Components { get; init; } = [];

    /// <summary>The shop-wide Wastage % it's priced with (CostingDefaults.WastagePercent): today's, or a version's then.</summary>
    public decimal WastagePercent { get; init; }
}

/// <summary>
/// What one sellable unit of a product brings into a composite at each basis, its materials at their buffered costs and at
/// their true ones: its recipe cost (the Sub-assemblies figure on its Costing tab), and its packaging cost (the Packaging
/// figure there), which the composite takes only when it includes its products' packaging.
/// </summary>
public sealed record ComponentCosts(decimal RecipeBuffered, decimal RecipeUnbuffered, decimal PackagingBuffered, decimal PackagingUnbuffered)
{
    public decimal RecipeAt(CostBasis basis) => basis == CostBasis.Unbuffered ? RecipeUnbuffered : RecipeBuffered;

    public decimal PackagingAt(CostBasis basis) => basis == CostBasis.Unbuffered ? PackagingUnbuffered : PackagingBuffered;
}

/// <summary>One product in a saved composite, with what one of it brings: its recipe and packaging costs for one sellable unit, as saved.</summary>
public sealed record SavedComponent(ProductComponent Component, ComponentCosts Costs)
{
    /// <summary>What one of the product brings at <paramref name="basis"/>.</summary>
    public decimal RecipeCostAt(CostBasis basis) => Costs.RecipeAt(basis);

    /// <summary>What the line brings into the composite's cost at <paramref name="basis"/>.</summary>
    public decimal CostAt(CostBasis basis) => CompositeProductRules.CalculateComponentCost(RecipeCostAt(basis), Component.Quantity);

    /// <summary>What the line brings into the composite's packaging at <paramref name="basis"/>, when it includes its products'.</summary>
    public decimal PackagingCostAt(CostBasis basis) =>
        CompositeProductRules.CalculateComponentPackagingCost(Costs.PackagingAt(basis), Component.Quantity);

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
            ? [.. Components.Select(c => Product.IncludesComponentPackaging
                ? new PrintedComponent(
                    c.Component.ComponentProduct!.Name,
                    c.Component.Quantity,
                    c.RecipeCostAt(CostBasis),
                    c.CostAt(CostBasis) + c.PackagingCostAt(CostBasis),
                    c.Costs.PackagingAt(CostBasis))
                : new PrintedComponent(c.Component.ComponentProduct!.Name, c.Component.Quantity, c.RecipeCostAt(CostBasis), c.CostAt(CostBasis)))]
            : null)
    {
        CostBasis = CostBasis,
    };
}

/// <summary>
/// Reads a product as it's saved, and turns it into what the Products screen shows: the one place a saved product is
/// read and priced. The Products screen reads the product it opens through it, Reports prices every product with it,
/// and Print reads a set of products with it, so all three agree. Repositories only: a read runs off the UI thread.
/// </summary>
public sealed class SavedProductReader(
    IPackagingLinkRepository packagingLinkRepository,
    IProductSubAssemblyUsageRepository usageRepository,
    IPackagingLineRepository packagingLineRepository,
    ICostingParametersRepository costingParametersRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository,
    IProductRepository productRepository,
    ICostingDefaultsRepository costingDefaultsRepository)
{
    /// <summary>
    /// The product's saved detail: its packaging link and lines, its recipe with its sections' lines and add-ins, a
    /// composite's products with their recipe costs, its costing, and the shop-wide wastage it's priced with.
    /// </summary>
    public async Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken) =>
        (await ReadDetailsAsync([productId], cancellationToken))[productId];

    /// <summary>
    /// Several products' saved details at once, by id: <see cref="ReadDetailAsync"/>'s, in a dozen or so queries however
    /// many products there are (2026-10-03). A product took nine or so queries of its own, and Reports and Print read every
    /// product that way, so their round trips grew with the list, and a composite's products each started reads of their
    /// own at once. The products a composite among them holds are read with it, however deep, and are in the result too.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, ProductDetail>> ReadDetailsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        // The products the composites among them hold, and the ones those hold: one read for each level of holding.
        Dictionary<int, IReadOnlyList<ProductComponent>> componentsByHolder = [];
        HashSet<int> productIdsRead = [.. productIds];
        List<int> holders = [.. productIdsRead];
        while (holders.Count > 0)
        {
            var components = await productRepository.GetComponentsForProductsAsync(holders, cancellationToken);
            foreach (var held in components.GroupBy(c => c.ProductId))
            {
                componentsByHolder[held.Key] = [.. held];
            }

            holders = [.. components.Select(c => c.ComponentProductId).Where(productIdsRead.Add)];
        }

        // The rest one at a time, not side by side (2026-10-28): a Save reads through here inside its unit of work, whose
        // reads share one connection (IUnitOfWork). Measured against the dev database (68 products), every product's
        // detail took 31 ms this way against 22 ms side by side, and one product's 13 ms against 12.
        List<int> ids = [.. productIdsRead];
        var linksByProduct = (await packagingLinkRepository.GetForProductsAsync(ids, cancellationToken)).ToDictionary(l => l.ProductId);
        var packagingByProduct = await packagingLineRepository.GetEffectiveForProductsAsync(ids, cancellationToken);
        var costingsByProduct = (await costingParametersRepository.GetForProductsAsync(ids, cancellationToken)).ToDictionary(c => c.ProductId);
        var addIns = await usageRepository.GetAddInsForProductsAsync(ids, cancellationToken);
        var usages = await usageRepository.GetForProductsAsync(ids, cancellationToken);
        var sectionLines = await subAssemblyLineRepository.GetForSubAssembliesAsync([.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);
        var wastagePercent = (await costingDefaultsRepository.GetAsync(cancellationToken)).WastagePercent;

        var usagesByProduct = usages.ToLookup(u => u.ProductId);
        var productIdByUsage = usages.ToDictionary(u => u.Id, u => u.ProductId);
        var addInsByProduct = addIns.ToLookup(a => productIdByUsage[a.UsageId]);
        var sectionLinesBySubAssembly = sectionLines.ToLookup(l => l.SubAssemblyId);

        // Each product put together from what was read, a composite's products before it: what one brings is its own
        // costing worked out, at both bases, once however many composites hold it.
        Dictionary<int, ProductDetail> details = [];
        Dictionary<int, ComponentCosts> costsByProduct = [];

        ProductDetail DetailOf(int productId, ImmutableHashSet<int> holding)
        {
            // Save refuses a composite that would hold itself, so this only guards against data changed outside the app,
            // which would otherwise read round the loop for ever.
            if (holding.Contains(productId))
            {
                throw new BusinessRuleException("A composite product holds itself, through the products in it. Take it out of one of them.");
            }

            if (details.TryGetValue(productId, out var built))
            {
                return built;
            }

            var productUsages = usagesByProduct[productId].ToList();
            var detail = new ProductDetail(
                linksByProduct.GetValueOrDefault(productId),
                productUsages,
                packagingByProduct[productId],
                costingsByProduct.GetValueOrDefault(productId),
                [.. productUsages.Select(u => u.SubAssemblyId).Distinct().SelectMany(id => sectionLinesBySubAssembly[id])],
                [.. addInsByProduct[productId]])
            {
                WastagePercent = wastagePercent,
                Components =
                [
                    .. componentsByHolder.GetValueOrDefault(productId, [])
                        .Select(c => new SavedComponent(c, CostsOf(c.ComponentProduct!, holding.Add(productId)))),
                ],
            };
            details[productId] = detail;
            return detail;
        }

        ComponentCosts CostsOf(Product product, ImmutableHashSet<int> holding)
        {
            if (!costsByProduct.TryGetValue(product.Id, out var costs))
            {
                costs = ComponentCostsOf(product, DetailOf(product.Id, holding));
                costsByProduct[product.Id] = costs;
            }

            return costs;
        }

        foreach (var productId in ids)
        {
            DetailOf(productId, []);
        }

        return details;
    }

    /// <summary>
    /// What one of each product brings into a composite, as it's saved: its recipe (sub-assemblies, ingredients) cost for
    /// one sellable unit, which is the Sub-assemblies figure on its Costing tab, and its packaging cost, the Packaging figure
    /// there, buffered and unbuffered. A product that's gone is left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, ComponentCosts>> ReadComponentCostsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, ComponentCosts>();
        }

        // Read again rather than taken from the screen's list, which may be older than a quantity required saved since.
        var products = await productRepository.GetByIdsAsync(productIds, cancellationToken);
        var details = await ReadDetailsAsync(productIds, cancellationToken);
        return products.ToDictionary(p => p.Id, p => ComponentCostsOf(p, details[p.Id]));
    }

    /// <summary>
    /// Its recipe and packaging costs at both bases, from one read: pricing it twice is only arithmetic. With
    /// <paramref name="materialsById"/>, its materials at those costs instead (the Price Simulator screen's).
    /// </summary>
    private static ComponentCosts ComponentCostsOf(Product product, ProductDetail detail, IReadOnlyDictionary<int, RawMaterial>? materialsById = null)
    {
        var buffered = Build(product, detail, CostBasis.Buffered, materialsById).Costs;
        var unbuffered = Build(product, detail, CostBasis.Unbuffered, materialsById).Costs;
        return new(buffered.SubAssemblies, unbuffered.SubAssemblies, buffered.Packaging, unbuffered.Packaging);
    }

    /// <summary>
    /// <paramref name="details"/> with every composite's products priced again with <paramref name="materialsById"/> in place
    /// of the materials they were read with (the Price Simulator screen, 2026-10-03): what a composite's products bring is worked out
    /// when they're read, so a different cost for one of their materials has to reach them here, however deeply they're held.
    /// A product holding none comes back as it was, since <see cref="Build"/> takes the materials for its own sections.
    /// </summary>
    public static IReadOnlyDictionary<int, ProductDetail> PricedAt(
        IReadOnlyDictionary<int, ProductDetail> details, IReadOnlyDictionary<int, RawMaterial> materialsById)
    {
        Dictionary<int, ProductDetail> priced = [];

        ProductDetail Priced(int productId, ImmutableHashSet<int> holding)
        {
            if (holding.Contains(productId))
            {
                throw new BusinessRuleException("A composite product holds itself, through the products in it. Take it out of one of them.");
            }

            if (priced.TryGetValue(productId, out var done))
            {
                return done;
            }

            var detail = details[productId];
            if (detail.Components.Count > 0)
            {
                detail = detail with
                {
                    Components =
                    [
                        .. detail.Components.Select(c => c with
                        {
                            Costs = ComponentCostsOf(c.Component.ComponentProduct!, Priced(c.Component.ComponentProductId, holding.Add(productId)), materialsById),
                        }),
                    ],
                };
            }

            priced[productId] = detail;
            return detail;
        }

        foreach (var productId in details.Keys)
        {
            Priced(productId, []);
        }

        return priced;
    }

    /// <summary><paramref name="product"/> as it's saved, read and priced at its buffered costs.</summary>
    public Task<SavedProduct> ReadAsync(Product product, CancellationToken cancellationToken) =>
        ReadAsync(product, CostBasis.Buffered, cancellationToken);

    /// <summary><paramref name="product"/> as it's saved, read and priced at <paramref name="basis"/>.</summary>
    public async Task<SavedProduct> ReadAsync(Product product, CostBasis basis, CancellationToken cancellationToken) =>
        Build(product, await ReadDetailAsync(product.Id, cancellationToken), basis);

    /// <summary>
    /// <paramref name="products"/> as they're saved, read together (<see cref="ReadDetailsAsync"/>) and each priced at
    /// <paramref name="basis"/>, in the same order: what Reports prices and a Print set prints.
    /// </summary>
    public async Task<IReadOnlyList<SavedProduct>> ReadAllAsync(IReadOnlyList<Product> products, CostBasis basis, CancellationToken cancellationToken)
    {
        if (products.Count == 0)
        {
            return [];
        }

        var details = await ReadDetailsAsync([.. products.Select(p => p.Id).Distinct()], cancellationToken);
        return [.. products.Select(p => Build(p, details[p.Id], basis))];
    }

    /// <summary>
    /// <paramref name="detail"/> as the screen shows it: each section with the ingredients and role it was read with,
    /// and the costing from the saved costing, the product's quantity per unit and its current price, as RecomputeCosts
    /// works it out from the form. A composite's recipe cost is its products', then its own sections'; its packaging is its
    /// own, and its products' too when it includes theirs.
    /// </summary>
    public static SavedProduct Build(Product product, ProductDetail detail) => Build(product, detail, CostBasis.Buffered);

    /// <summary>The same, priced at <paramref name="basis"/>: its materials' buffered costs, or their true ones.</summary>
    public static SavedProduct Build(Product product, ProductDetail detail, CostBasis basis) => Build(product, detail, basis, null);

    /// <summary>
    /// The same, with <paramref name="materialsById"/> in place of the materials its sections and packaging were read with,
    /// where it has them: the Price Simulator screen's materials at costs that aren't real. A composite's products are priced as
    /// <paramref name="detail"/> has them, so give it a detail from <see cref="PricedAt"/>.
    /// </summary>
    public static SavedProduct Build(Product product, ProductDetail detail, CostBasis basis, IReadOnlyDictionary<int, RawMaterial>? materialsById)
    {
        var linesBySubAssembly = detail.SubAssemblyLines.ToLookup(l => l.SubAssemblyId);
        var addInsByUsage = detail.AddIns.ToLookup(a => a.UsageId);
        List<ProductUsageEdit> sections =
        [
            .. detail.Usages
                .Select(u => ProductUsageEdit.FromSaved(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], u.SubAssembly!.Role, product.Name, materialsById))
                .Order(ProductUsageEdit.RecipeOrder),
        ];
        List<PackagingLineRow> packagingLines = [.. detail.Lines.Select(l => PackagingLineRow.FromSaved(l, basis))];
        if (materialsById is not null)
        {
            PackagingLineRow.Resolve(packagingLines, materialsById);
        }

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
            product.IsComposite && sections.Count > 0,
            product.IncludesComponentPackaging ? detail.Components.Sum(c => c.PackagingCostAt(basis)) : null,
            detail.WastagePercent);

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
