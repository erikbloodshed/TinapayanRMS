using System.Collections.Immutable;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>Everything a product's detail shows, as it's saved: what <see cref="SavedProductReader.ReadDetailAsync"/> reads.</summary>
/// <param name="PackagingLink">What its packaging per piece links, or null for its own lines.</param>
public sealed record ProductDetail(
    PackagingLink? PackagingLink,
    IReadOnlyList<ProductSubAssemblyUsage> Usages,
    IReadOnlyList<PackagingLine> Lines,
    CostingParameters? Costing,
    IReadOnlyList<SubAssemblyLine> SubAssemblyLines,
    IReadOnlyList<UsageAddInLine> AddIns)
{
    /// <summary>Its lines that are others of its sections (UsageSectionLine), each in its section's order; none for most.</summary>
    public IReadOnlyList<UsageSectionLine> SectionUses { get; init; } = [];

    /// <summary>A composite's products, in its Products table's order, each with its recipe and packaging costs as saved; none for a product made of sub-assemblies.</summary>
    public IReadOnlyList<SavedComponent> Components { get; init; } = [];

    /// <summary>
    /// Its bulk packaging (PackagingLine.IsBulk), its lines for one bulk pack in order: the packaging set's it links
    /// (<see cref="BulkPackagingLink"/>), or its own; none for a product without.
    /// </summary>
    public IReadOnlyList<PackagingLine> BulkLines { get; init; } = [];

    /// <summary>The packaging set its bulk packaging links (PackagingLink.IsBulk), with the set; null for its own lines, or none.</summary>
    public PackagingLink? BulkPackagingLink { get; init; }

    /// <summary>
    /// The Wastage % it's priced with: its category's when that sets one (ProductCategory.WastagePercent), else the shop-wide
    /// one (CostingDefaults.WastagePercent); today's, or a version's then.
    /// </summary>
    public decimal WastagePercent { get; init; }

    /// <summary>The VAT it's priced with: the shop's (CostingDefaults.VatPercent); today's, or a version's then.</summary>
    public decimal VatPercent { get; init; }

    /// <summary>What its rounded selling price is rounded up to a multiple of (CostingDefaults.PriceRoundingStep): today's; 0 for a version's.</summary>
    public decimal PriceRoundingStep { get; init; }

    /// <summary>
    /// A composite that holds itself, through the products it holds, as only data changed outside the app can leave one
    /// (CompositeProductRules.HoldsItselfText): the product that closes the loop brings nothing at that point.
    /// </summary>
    public bool HoldsItself { get; init; }

    /// <summary>
    /// Its packaging links come round in a loop, as only data changed outside the app can leave them
    /// (PackagingLinkRules.LinkedRoundInALoopText): it shows no packaging, and none is costed.
    /// </summary>
    public bool PackagingLinkedRoundInALoop { get; init; }
}

/// <summary>
/// What one sellable unit of a product brings into a composite at each basis, its materials at their buffered costs and at
/// their true ones: its recipe cost (the Sub-assemblies figure on its Costing tab), and its packaging cost (the Packaging
/// figure there), which the composite takes only when it includes its products' packaging.
/// </summary>
public sealed record ComponentCosts(decimal RecipeBuffered, decimal RecipeUnbuffered, decimal PackagingBuffered, decimal PackagingUnbuffered)
{
    /// <summary>What a product brings where it would close a loop of composites holding each other: nothing.</summary>
    public static ComponentCosts None { get; } = new(0m, 0m, 0m, 0m);

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
    public string LineName => CompositeProductRules.LineName(Component.ComponentProduct!.Name, Component.Quantity);
}

/// <summary>
/// A saved product, read and priced: its sections in recipe order, its packaging, and its costing worked out to the
/// selling price, all immutable. What Reports prices, what a set of products prints, and what the servers return.
/// </summary>
/// <param name="PackagingLinkedTo">The Packaging tab's "Linked to …" banner, or null for the product's own lines.</param>
/// <param name="CostBasis">Which of its materials' costs it's priced at: buffered, unless the Products screen is switched to unbuffered.</param>
public sealed record SavedProduct(
    Product Product,
    IReadOnlyList<PricedSection> Sections,
    IReadOnlyList<PricedPackagingLine> PackagingLines,
    bool HasPackaging,
    string? PackagingLinkedTo,
    ProductCostSummary Costs,
    CostBasis CostBasis = CostBasis.Buffered)
{
    /// <summary>A composite's products, each with its recipe cost; none for a product made of sub-assemblies.</summary>
    public IReadOnlyList<SavedComponent> Components { get; init; } = [];

    /// <summary>Its bulk packaging, priced, whose share per unit is in its packaging cost; null for a product without.</summary>
    public PricedBulkPackaging? BulkPackaging { get; init; }

    /// <summary>"Linked to Box of 6 (Sub-Assemblies)" while its bulk packaging links a packaging set; null for its own lines, or none.</summary>
    public string? BulkPackagingLinkedTo { get; init; }

    /// <summary>
    /// Its sections with no weight per piece or yield to cost it by (<see cref="PricedSection.LacksPieceSize"/>), by name, in
    /// recipe order: each costs it nothing per piece, so its cost and price leave that section out until one is typed. The
    /// Products list and Reports flag them. Never one with none that another section uses, which costs through that one.
    /// </summary>
    public IReadOnlyList<string> SectionsWithoutPieceSize => [.. Sections.Where(s => s.LacksPieceSize).Select(s => s.SectionName)];
}

/// <summary>
/// Reads a product as it's saved, and turns it into what the Products screen shows: the one place a saved product is
/// read and priced. The Products screen reads the product it opens through it, Reports prices every product with it,
/// and Print reads a set of products with it, so all three agree. Repositories only: a read runs off the UI thread.
/// <para>
/// A read takes a dozen queries, and they read as of one moment (IUnitOfWork.ReadSnapshotAsync): a
/// Save replaces a product's sections with new rows, and one that committed between two of the queries had left add-ins
/// whose section the next query no longer found, which failed the whole read (Reports, Print, the Recipe API's recipes)
/// and could mix figures from before and after it.
/// </para>
/// </summary>
public sealed class SavedProductReader(
    IPackagingLinkRepository packagingLinkRepository,
    IProductSubAssemblyUsageRepository usageRepository,
    IPackagingLineRepository packagingLineRepository,
    ICostingParametersRepository costingParametersRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository,
    IProductRepository productRepository,
    ICostingDefaultsRepository costingDefaultsRepository,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// The product's saved detail: its packaging link and lines, its recipe with its sections' lines and add-ins, a
    /// composite's products with their recipe costs, its costing, and the shop-wide wastage it's priced with.
    /// </summary>
    public async Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken) =>
        (await ReadDetailsAsync([productId], cancellationToken))[productId];

    /// <summary>
    /// Several products' saved details at once, by id: <see cref="ReadDetailAsync"/>'s, in a dozen or so queries however
    /// many products there are, rather than nine or so a product, which for Reports and Print would grow with the list. The products a composite among them holds are read with it, however deep, and are in the result too.
    /// Every query reads as of one moment (IUnitOfWork.ReadSnapshotAsync).
    /// </summary>
    public Task<IReadOnlyDictionary<int, ProductDetail>> ReadDetailsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        unitOfWork.ReadSnapshotAsync(() => ReadDetailsInSnapshotAsync(productIds, cancellationToken), cancellationToken);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Baseline of 2026-11-20 (ADR 0004): split it when it next changes for another reason.")]
    private async Task<IReadOnlyDictionary<int, ProductDetail>> ReadDetailsInSnapshotAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
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

        // The rest one at a time, not side by side: they share the snapshot's connection, or a Save's inside its unit of work
        // (IUnitOfWork). It costs little: every product's detail, 68 of them, in 31 ms against 22 side by side.
        List<int> ids = [.. productIdsRead];
        var links = await packagingLinkRepository.GetForProductsAsync(ids, cancellationToken);
        var linksByProduct = links.Where(l => !l.IsBulk).ToDictionary(l => l.ProductId);
        var bulkLinksByProduct = links.Where(l => l.IsBulk).ToDictionary(l => l.ProductId);
        var packaging = await packagingLineRepository.GetEffectiveForProductsAsync(ids, cancellationToken);
        var packagingByProduct = packaging.Lines;
        var costingsByProduct = (await costingParametersRepository.GetForProductsAsync(ids, cancellationToken)).ToDictionary(c => c.ProductId);
        var usageLines = await usageRepository.GetLinesForProductsAsync(ids, cancellationToken);
        var usages = await usageRepository.GetForProductsAsync(ids, cancellationToken);
        var sectionLines = await subAssemblyLineRepository.GetForSubAssembliesAsync([.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);
        var costingDefaults = await costingDefaultsRepository.GetAsync(cancellationToken);
        var categoryWastage = await productRepository.GetCategoryWastageForProductsAsync(ids, cancellationToken);

        var usagesByProduct = usages.ToLookup(u => u.ProductId);
        var productIdByUsage = usages.ToDictionary(u => u.Id, u => u.ProductId);
        var addInsByProduct = usageLines.AddIns.ToLookup(a => productIdByUsage[a.UsageId]);
        var sectionUsesByProduct = usageLines.SectionLines.ToLookup(l => productIdByUsage[l.UsageId]);
        var sectionLinesBySubAssembly = sectionLines.ToLookup(l => l.SubAssemblyId);

        // Each product put together from what was read, a composite's products before it: what one brings is its own
        // costing worked out, at both bases, once however many composites hold it.
        Dictionary<int, ProductDetail> details = [];
        Dictionary<int, ComponentCosts> costsByProduct = [];

        // A composite holding itself, through the products it holds, can only come of data changed outside the app, since
        // Save refuses it. It's read all the same, its loop cut where it closes, rather than refused: a refusal would fail the
        // whole read, every product's, for Reports, Print, the Products list and the servers.
        Dictionary<int, IReadOnlyList<int>> heldIds = componentsByHolder.ToDictionary(e => e.Key, e => (IReadOnlyList<int>)[.. e.Value.Select(c => c.ComponentProductId)]);
        bool HoldsItself(int productId) =>
            heldIds.TryGetValue(productId, out var held) && held.Any(id => id == productId || CompositeProductRules.Contains(id, productId, heldIds));

        ProductDetail DetailOf(int productId, ImmutableHashSet<int> holding)
        {
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
                SectionUses = [.. sectionUsesByProduct[productId]],

                // Its category's, when that sets one; a composite's own, never its products'.
                WastagePercent = categoryWastage.TryGetValue(productId, out var wastage) ? wastage : costingDefaults.WastagePercent,
                VatPercent = costingDefaults.VatPercent,
                PriceRoundingStep = costingDefaults.PriceRoundingStep,
                Components =
                [
                    .. componentsByHolder.GetValueOrDefault(productId, [])
                        .Select(c => new SavedComponent(
                            c,
                            ClosesLoop(c.ComponentProductId, productId, holding) ? ComponentCosts.None : CostsOf(c.ComponentProduct!, holding.Add(productId)))),
                ],
                HoldsItself = HoldsItself(productId),
                PackagingLinkedRoundInALoop = packaging.LinkedRoundInALoop.Contains(productId),
                BulkLines = packaging.Bulk.GetValueOrDefault(productId, []),
                BulkPackagingLink = bulkLinksByProduct.GetValueOrDefault(productId),
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

        // Read again rather than taken from the screen's list, which may be older than a quantity required saved since: the
        // products and their details as of the same moment.
        return await unitOfWork.ReadSnapshotAsync<IReadOnlyDictionary<int, ComponentCosts>>(async () =>
        {
            var products = await productRepository.GetByIdsAsync(productIds, cancellationToken);
            var details = await ReadDetailsAsync(productIds, cancellationToken);
            return products.ToDictionary(p => p.Id, p => ComponentCostsOf(p, details[p.Id]));
        }, cancellationToken);
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
    /// of the materials they were read with (the Price Simulator screen): what a composite's products bring is worked out
    /// when they're read, so a different cost for one of their materials has to reach them here, however deeply they're held.
    /// A product holding none comes back as it was, since <see cref="Build(Product, ProductDetail, CostBasis, IReadOnlyDictionary{int, RawMaterial})"/> takes the materials for its own sections.
    /// </summary>
    public static IReadOnlyDictionary<int, ProductDetail> PricedAt(
        IReadOnlyDictionary<int, ProductDetail> details, IReadOnlyDictionary<int, RawMaterial> materialsById)
    {
        Dictionary<int, ProductDetail> priced = [];

        ProductDetail Priced(int productId, ImmutableHashSet<int> holding)
        {
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
                            Costs = ClosesLoop(c.Component.ComponentProductId, productId, holding)
                                ? ComponentCosts.None
                                : ComponentCostsOf(c.Component.ComponentProduct!, Priced(c.Component.ComponentProductId, holding.Add(productId)), materialsById),
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

    /// <summary>
    /// Whether <paramref name="componentId"/>, held by <paramref name="productId"/>, closes a loop of composites holding each
    /// other: it's that product itself, or one of those being put together above it (<paramref name="holding"/>).
    /// </summary>
    private static bool ClosesLoop(int componentId, int productId, ImmutableHashSet<int> holding) =>
        componentId == productId || holding.Contains(componentId);

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
    /// and the costing from the saved costing, the product's quantity per unit and its current price, as the Products
    /// screen works it out from the form (ProductDraft.CostingInputs). A composite's recipe cost is its products', then its own sections'; its packaging is its
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
        var sectionUsesByUsage = detail.SectionUses.ToLookup(l => l.UsageId);

        // Two sections in one role are told apart by their labels, "Dough" beside "Dough: Paste", as the screen tells them.
        var repeatedRoles = PricedSection.RepeatedRoles(detail.Usages.Select(u => u.SubAssembly!.Role?.Id ?? 0));
        // A section on another's lines costs that one its grams, and its own pieces come of what's left of it.
        var sections = SectionUses.Apply(
        [
            .. detail.Usages
                .Select(u =>
                {
                    var role = u.SubAssembly!.Role;
                    return PricedSection.FromSaved(
                        u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], sectionUsesByUsage[u.Id], role, product.Name, repeatedRoles.Contains(role?.Id ?? 0), materialsById);
                })
                .Order(PricedSection.RecipeOrder),
        ]);
        List<PricedPackagingLine> packagingLines = [.. detail.Lines.Select(l => PricedPackagingLine.FromSaved(l, basis, materialsById))];
        var bulk = PricedBulkPackaging.FromSaved(detail.BulkLines, product.BulkPackagingQuantity, basis, materialsById);

        var costing = detail.Costing;
        var costs = new ProductCostSummary(new ProductCostInputs
        {
            Parts =
            [
                .. detail.Components.Select(c => (c.LineName, c.CostAt(basis))),
                .. sections.Select(u => (u.SectionName, u.CostPerPieceAt(basis))),
            ],
            QuantityRequired = product.QuantityRequired,
            PackagingCosts = [.. packagingLines.Select(l => l.CostPerUnit), .. bulk is null ? [] : new[] { bulk.CostPerUnit }],
            ComponentPackaging = product.IncludesComponentPackaging ? detail.Components.Sum(c => c.PackagingCostAt(basis)) : null,
            LaborPercent = costing?.LaborPercent ?? 0m,
            OverheadPercent = costing?.OverheadPercent ?? 0m,
            AdministrativePercent = costing?.AdministrativePercent ?? 0m,
            MarkupPercent = costing?.MarkupPercent ?? 0m,
            VatPercent = detail.VatPercent,
            WastagePercent = detail.WastagePercent,
            PriceRoundingStep = detail.PriceRoundingStep,
            CurrentPrice = product.CurrentPrice,
            IsComposite = product.IsComposite,
            HasOwnSections = product.IsComposite && sections.Count > 0,
        });

        return new SavedProduct(
            product,
            sections,
            packagingLines,
            detail.PackagingLink is not null || detail.Lines.Count > 0 || bulk is not null,
            detail.PackagingLink is { } link ? PackagingLinkTarget.From(link).LinkedToText : null,
            costs,
            basis)
        {
            Components = detail.Components,
            BulkPackaging = bulk,
            BulkPackagingLinkedTo = detail.BulkPackagingLink is { } bulkLink ? PackagingLinkTarget.From(bulkLink).LinkedToText : null,
        };
    }
}
