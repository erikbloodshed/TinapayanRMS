using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing.Versions;

/// <summary>
/// What a product's Save wrote: its id, the product and its detail as saved, which the form shows next, and the names of
/// the products sharing its changes that got a version.
/// </summary>
public sealed record ProductSaved(int Id, Product Product, ProductDetail Detail, IReadOnlyList<string> SharingVersioned);

/// <summary>What a sub-assembly's Save wrote: its id, and the names of the products using it that got a version.</summary>
public sealed record SubAssemblySaved(int Id, IReadOnlyList<string> ProductsVersioned);

/// <summary>
/// The saves that record product versions, each in one unit of work with the versions it records (IUnitOfWork), so a
/// save and its versions are written together or not at all: a version that can't be recorded takes its save back. Its
/// reads run one at a time, as a unit of work's must.
/// </summary>
public sealed class VersionedSaves(
    IUnitOfWork unitOfWork,
    IProductRepository productRepository,
    IProductVersionRepository versionRepository,
    ICostingParametersRepository costingParametersRepository,
    SavedProductReader savedProducts,
    ProductVersionRecorder versionRecorder)
{
    /// <summary>How the Products list's Apply Costing Defaults notes the version it records.</summary>
    public const string CostingDefaultsNote = "Costing defaults applied";

    /// <summary>
    /// The Products list's Apply Costing Defaults: each product in <paramref name="costings"/> whose costing isn't already
    /// the labor, overhead, administrative and markup percentages given for it (its category's costing defaults, as the
    /// question that confirmed them named them: CostingRules.DefaultCostingFor) takes them, gets a version noted
    /// <see cref="CostingDefaultsNote"/> (with how it stood before, for one with no versions yet) and is stamped as saved now,
    /// all in one unit of work. One with no costing saved counts as all zero; a product given twice takes its first. Returns
    /// the names of the ones it changed, in the order given.
    /// </summary>
    public Task<IReadOnlyList<string>> ApplyCostingAsync(
        IReadOnlyList<(int ProductId, CostingParameters Costing)> costings, DateTime savedAt, CancellationToken cancellationToken) =>
        unitOfWork.RunAsync<IReadOnlyList<string>>(async () =>
        {
            // The products are read and written together, in the same few commands however many there are (one more for each
            // other set of percentages): one at a time, it took some 30 a product, over 2,400 for the shop's 84, and 1.6 s
            // (2026-11-13).
            static (decimal, decimal, decimal, decimal) Percentages(CostingParameters? costing) =>
                (costing?.LaborPercent ?? 0m, costing?.OverheadPercent ?? 0m, costing?.AdministrativePercent ?? 0m, costing?.MarkupPercent ?? 0m);
            Dictionary<int, (decimal, decimal, decimal, decimal)> wanted = [];
            foreach (var (productId, costing) in costings)
            {
                wanted.TryAdd(productId, Percentages(costing));
            }

            (decimal, decimal, decimal, decimal) Wanted(Product product) => wanted[product.Id];

            List<int> given = [.. wanted.Keys];
            var saved = (await costingParametersRepository.GetForProductsAsync(given, cancellationToken)).ToDictionary(c => c.ProductId);
            var productsById = (await productRepository.GetByIdsAsync([.. given.Where(id => Percentages(saved.GetValueOrDefault(id)) != wanted[id])], cancellationToken))
                .ToDictionary(p => p.Id);
            List<Product> changing = [.. given.Where(productsById.ContainsKey).Select(id => productsById[id])];
            if (changing.Count == 0)
            {
                return [];
            }

            List<int> changingIds = [.. changing.Select(p => p.Id)];
            var before = await savedProducts.ReadDetailsAsync(changingIds, cancellationToken);
            foreach (var group in changing.GroupBy(Wanted))
            {
                var (labor, overhead, administrative, markup) = group.Key;
                await costingParametersRepository.SetForProductsAsync([.. group.Select(p => p.Id)], labor, overhead, administrative, markup, cancellationToken);
            }

            var after = await savedProducts.ReadDetailsAsync(changingIds, cancellationToken);

            // One with no versions yet gets how it stood before first, dated when it was last saved, as a Save's would be
            // (RecordVersionBeforeHistoryAsync).
            await versionRepository.AddRangeAsync(
                [
                    .. changing.Select(product => new ProductVersionDraft(product.Id, ProductVersionSnapshot.From(product, after[product.Id]), savedAt, CostingDefaultsNote)
                    {
                        Baseline = new(product.Id, ProductVersionSnapshot.From(product, before[product.Id]), product.UpdatedAt ?? savedAt, ProductVersionRules.BaselineNote),
                    }),
                ],
                cancellationToken);
            await productRepository.StampSavedAsync(changingIds, cancellationToken);
            return [.. changing.Select(p => p.Name)];
        }, cancellationToken);

    /// <summary>
    /// The Products screen's Save (IProductRepository.SaveAsync), with its versions: for a saved product with none yet, how
    /// it stood until now first; then the product as saved, unless its recipe and price are its latest version's; and,
    /// when <paramref name="changesSharing"/> (it edits linked lines or its packaging), a version of each product sharing
    /// them that this Save changes, noted with its name.
    /// </summary>
    public Task<ProductSaved> SaveProductAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        BulkPackagingDraft? bulkPackaging,
        CostingParameters? costing,
        bool changesSharing,
        DateTime savedAt,
        CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async () =>
        {
            var sharing = ProductVersionCapture.Empty;
            if (product.Id != 0)
            {
                await RecordVersionBeforeHistoryAsync(product.Id, savedAt, cancellationToken);
                if (changesSharing)
                {
                    sharing = await versionRecorder.CaptureAsync([], [product.Id], cancellationToken);
                }
            }

            var id = await productRepository.SaveAsync(product, recipe, packagingLink, packagingLines, bulkPackaging, costing, cancellationToken);
            var saved = await productRepository.GetByIdAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Product {id} wasn't there to read back after its Save.");
            var detail = await savedProducts.ReadDetailAsync(id, cancellationToken);
            await versionRepository.AddAsync(id, ProductVersionSnapshot.From(saved, detail), savedAt, null, cancellationToken);

            var sharingVersioned = await versionRecorder.RecordChangesAsync(
                sharing, $"Changed when {product.Name} was saved", savedAt, cancellationToken);
            return new ProductSaved(id, saved, detail, sharingVersioned);
        }, cancellationToken);

    /// <summary>
    /// A product made from <paramref name="sourceProductId"/> by <paramref name="make"/> (IProductCloner's Clone or Create
    /// Variant), with its first version, noted "<paramref name="madeAs"/>" the source (ProductVersionRecorder.RecordMadeFromAsync).
    /// </summary>
    public Task<int> MakeProductFromAsync(
        int sourceProductId,
        string name,
        Func<int, string, CancellationToken, Task<int>> make,
        string madeAs,
        DateTime savedAt,
        CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async () =>
        {
            var newId = await make(sourceProductId, name, cancellationToken);
            await versionRecorder.RecordMadeFromAsync(newId, sourceProductId, madeAs, savedAt, cancellationToken);
            return newId;
        }, cancellationToken);

    /// <summary>
    /// The Sub-Assemblies screen's Save, made by <paramref name="save"/>, with a version of each product using the
    /// sub-assembly that it changes, noted "<paramref name="name"/> changed on Sub-Assemblies". A new one has none yet.
    /// </summary>
    public Task<SubAssemblySaved> SaveSubAssemblyAsync(
        int subAssemblyId, string name, Func<CancellationToken, Task<int>> save, DateTime savedAt, CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async () =>
        {
            var usingProducts = subAssemblyId == 0
                ? ProductVersionCapture.Empty
                : await versionRecorder.CaptureAsync([subAssemblyId], [], cancellationToken);
            var id = await save(cancellationToken);
            var versioned = await versionRecorder.RecordChangesAsync(
                usingProducts, $"{name} changed on Sub-Assemblies", savedAt, cancellationToken);
            return new SubAssemblySaved(id, versioned);
        }, cancellationToken);

    /// <summary>
    /// Before the first Save since versions began to be kept: the product as it's saved now becomes its first version,
    /// dated when it was last saved, priced at what its materials cost now. Nothing when it has versions already.
    /// </summary>
    private async Task RecordVersionBeforeHistoryAsync(int productId, DateTime savedAt, CancellationToken cancellationToken)
    {
        if (await versionRepository.HasAnyAsync(productId, cancellationToken)
            || await productRepository.GetByIdAsync(productId, cancellationToken) is not { } product)
        {
            return;
        }

        var detail = await savedProducts.ReadDetailAsync(productId, cancellationToken);
        await versionRepository.AddAsync(
            productId, ProductVersionSnapshot.From(product, detail), product.UpdatedAt ?? savedAt, ProductVersionRules.BaselineNote, cancellationToken);
    }
}
