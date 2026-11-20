using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.Products;

/// <summary>What the Products screen reads: its load, a product and its detail as saved, and what its forms' parts show from elsewhere.</summary>
public interface IProductQueries
{
    /// <summary>
    /// Everything a load reads, side by side: the sub-assemblies <paramref name="linkedIds"/> and their lines, the packaging
    /// links of <paramref name="detailProductIds"/> (the products the open forms show) and what each of
    /// <paramref name="componentIds"/> brings to a composite, with the lists every load reads.
    /// </summary>
    Task<ProductsLoad> ReadLoadAsync(
        IReadOnlyCollection<int> detailProductIds, IReadOnlyCollection<int> linkedIds, IReadOnlyCollection<int> componentIds, CancellationToken cancellationToken);

    /// <summary>
    /// The packaging lines each of <paramref name="productIds"/> shows for one sellable unit as they're saved now, through
    /// its link, the linked set's or product's, by product: for every open form at once, in a few queries however many
    /// there are. A product whose links come round again, which only an edit outside the app can leave, isn't among them:
    /// its form keeps the lines it shows.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ReadLinkedPackagingAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>A product as saved, or null when it's gone.</summary>
    Task<Product?> ReadAsync(int productId, CancellationToken cancellationToken);

    /// <summary>The products of <paramref name="productIds"/> as saved, deleted ones included.</summary>
    Task<IReadOnlyList<Product>> ReadManyAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>What a saved product's form shows beyond the list's copy: its recipe, packaging, costing and products (SavedProductReader).</summary>
    Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken);

    /// <summary>What each of <paramref name="productIds"/> brings to a composite holding it, by id.</summary>
    Task<IReadOnlyDictionary<int, ComponentCosts>> ReadComponentCostsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>Each of <paramref name="products"/> priced as saved at <paramref name="basis"/>, as Reports prices them.</summary>
    Task<IReadOnlyList<SavedProduct>> PriceAsSavedAsync(IReadOnlyList<Product> products, CostBasis basis, CancellationToken cancellationToken);

    /// <summary>Settings' costing defaults.</summary>
    Task<CostingDefaults> ReadCostingDefaultsAsync(CancellationToken cancellationToken);

    /// <summary>The product categories.</summary>
    Task<IReadOnlyList<ProductCategory>> ReadCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>The list's folders, every stage's.</summary>
    Task<IReadOnlyList<ProductFolder>> ReadFoldersAsync(CancellationToken cancellationToken);

    /// <summary>Every Made category, which Make In-House Ingredient files the new ingredient in.</summary>
    Task<IReadOnlyList<RawMaterialCategory>> ReadMadeCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>A sub-assembly's lines as saved, which a section linking it or copied from it shows.</summary>
    Task<IReadOnlyList<SubAssemblyLine>> ReadSubAssemblyLinesAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>The sub-assemblies of <paramref name="subAssemblyIds"/> as they're saved now, and their lines, side by side.</summary>
    Task<LinkedSections> ReadLinkedSectionsAsync(IReadOnlyCollection<int> subAssemblyIds, CancellationToken cancellationToken);

    /// <summary>A packaging set's lines as saved.</summary>
    Task<IReadOnlyList<SubAssemblyPackagingLine>> ReadPackagingSetLinesAsync(int setId, CancellationToken cancellationToken);

    /// <summary>The packaging a product shows for one sellable unit as saved: its own, or through its link.</summary>
    Task<IReadOnlyList<PackagingLine>> ReadEffectivePackagingAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Who else uses each standalone sub-assembly: the products linking it, and the made ingredients made from it.</summary>
    Task<SectionUsers> ReadSectionUsersAsync(CancellationToken cancellationToken);

    /// <summary>A product's versions, with the product and its detail as saved now, side by side; null when it's gone.</summary>
    Task<ProductHistory?> ReadHistoryAsync(int productId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IProductQueries"/>
public sealed class ProductQueries(
    IProductRepository productRepository,
    IProductCategoryRepository categoryRepository,
    IProductFolderRepository folderRepository,
    IProductVersionRepository versionRepository,
    IRawMaterialRepository rawMaterialRepository,
    IRawMaterialCategoryRepository rawMaterialCategoryRepository,
    ISubAssemblyRepository subAssemblyRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository,
    ISubAssemblyRoleRepository subAssemblyRoleRepository,
    ICostingDefaultsRepository costingDefaultsRepository,
    IPackagingLinkRepository packagingLinkRepository,
    IPackagingLineRepository packagingLineRepository,
    SavedProductReader savedProducts) : IProductQueries
{
    public async Task<ProductsLoad> ReadLoadAsync(
        IReadOnlyCollection<int> detailProductIds, IReadOnlyCollection<int> linkedIds, IReadOnlyCollection<int> componentIds, CancellationToken cancellationToken)
    {
        // All at once, on the thread pool: each read is mostly a round trip's wait, so one after another they took
        // several times as long as the slowest of them, and the UI thread came back after each to start the next.
        // Each has its own context (AddDbContextFactory).
        var rolesRead = Task.Run(() => subAssemblyRoleRepository.GetAllAsync(cancellationToken), cancellationToken);
        var categoriesRead = Task.Run(() => categoryRepository.GetAllAsync(cancellationToken), cancellationToken);
        var foldersRead = Task.Run(() => folderRepository.GetAllAsync(cancellationToken), cancellationToken);
        var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(cancellationToken), cancellationToken);
        var productsRead = Task.Run(() => productRepository.GetAllAsync(cancellationToken), cancellationToken);
        var deletedRead = Task.Run(() => productRepository.GetDeletedAsync(cancellationToken), cancellationToken);
        var linkableRead = Task.Run(() => subAssemblyRepository.GetLinkableAsync(cancellationToken), cancellationToken);
        var standaloneRead = Task.Run(() => subAssemblyRepository.GetStandaloneAsync(cancellationToken), cancellationToken);
        var costingDefaultsRead = Task.Run(() => costingDefaultsRepository.GetAsync(cancellationToken), cancellationToken);
        var linkedSubAssembliesRead = Task.Run(() => subAssemblyRepository.GetByIdsAsync(linkedIds, cancellationToken), cancellationToken);
        var linkedLinesRead = Task.Run(() => subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, cancellationToken), cancellationToken);

        // Every open form's packaging link in one query, however many tabs are open.
        var packagingLinksRead = detailProductIds.Count == 0
            ? Task.FromResult<IReadOnlyList<PackagingLink>>([])
            : Task.Run(() => packagingLinkRepository.GetForProductsAsync(detailProductIds, cancellationToken), cancellationToken);
        var componentLinksRead = Task.Run(() => productRepository.GetAllComponentsAsync(cancellationToken), cancellationToken);
        var componentCostsRead = Task.Run(() => savedProducts.ReadComponentCostsAsync(componentIds, cancellationToken), cancellationToken);
        await Task.WhenAll(
            rolesRead, categoriesRead, foldersRead, rawMaterialsRead, productsRead, deletedRead, linkableRead, standaloneRead, costingDefaultsRead,
            linkedSubAssembliesRead, linkedLinesRead, packagingLinksRead, componentLinksRead, componentCostsRead);

        return new ProductsLoad(
            Roles: await rolesRead,
            Categories: await categoriesRead,
            Folders: await foldersRead,
            RawMaterials: await rawMaterialsRead,
            Products: await productsRead,
            DeletedProducts: await deletedRead,
            Linkable: await linkableRead,
            PackagingSets: [.. (await standaloneRead).Where(s => s.Role?.IsPackaging == true)],
            CostingDefaults: await costingDefaultsRead,
            LinkedSubAssemblies: await linkedSubAssembliesRead,
            LinkedLines: await linkedLinesRead,
            PackagingLinks: (await packagingLinksRead).ToDictionary(link => link.ProductId),
            ComponentLinks: await componentLinksRead,
            ComponentCosts: await componentCostsRead);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ReadLinkedPackagingAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<PackagingLine>>();
        }

        var effective = await packagingLineRepository.GetEffectiveForProductsAsync(productIds, cancellationToken);
        return effective.Lines
            .Where(lines => !effective.LinkedRoundInALoop.Contains(lines.Key))
            .ToDictionary(lines => lines.Key, lines => lines.Value);
    }

    public Task<Product?> ReadAsync(int productId, CancellationToken cancellationToken) => productRepository.GetByIdAsync(productId, cancellationToken);

    public Task<IReadOnlyList<Product>> ReadManyAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productRepository.GetByIdsAsync(productIds, cancellationToken);

    public Task<ProductDetail> ReadDetailAsync(int productId, CancellationToken cancellationToken) => savedProducts.ReadDetailAsync(productId, cancellationToken);

    public Task<IReadOnlyDictionary<int, ComponentCosts>> ReadComponentCostsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        savedProducts.ReadComponentCostsAsync(productIds, cancellationToken);

    public Task<IReadOnlyList<SavedProduct>> PriceAsSavedAsync(IReadOnlyList<Product> products, CostBasis basis, CancellationToken cancellationToken) =>
        savedProducts.ReadAllAsync(products, basis, cancellationToken);

    public Task<CostingDefaults> ReadCostingDefaultsAsync(CancellationToken cancellationToken) => costingDefaultsRepository.GetAsync(cancellationToken);

    public Task<IReadOnlyList<ProductCategory>> ReadCategoriesAsync(CancellationToken cancellationToken) => categoryRepository.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<ProductFolder>> ReadFoldersAsync(CancellationToken cancellationToken) => folderRepository.GetAllAsync(cancellationToken);

    public async Task<IReadOnlyList<RawMaterialCategory>> ReadMadeCategoriesAsync(CancellationToken cancellationToken) =>
        [.. (await rawMaterialCategoryRepository.GetAllAsync(cancellationToken)).Where(c => c.Type == RawMaterialCategoryType.Made)];

    public Task<IReadOnlyList<SubAssemblyLine>> ReadSubAssemblyLinesAsync(int subAssemblyId, CancellationToken cancellationToken) =>
        subAssemblyLineRepository.GetForSubAssembliesAsync([subAssemblyId], cancellationToken);

    public async Task<LinkedSections> ReadLinkedSectionsAsync(IReadOnlyCollection<int> subAssemblyIds, CancellationToken cancellationToken)
    {
        var subAssembliesRead = Task.Run(() => subAssemblyRepository.GetByIdsAsync(subAssemblyIds, cancellationToken), cancellationToken);
        var linesRead = Task.Run(() => subAssemblyLineRepository.GetForSubAssembliesAsync(subAssemblyIds, cancellationToken), cancellationToken);
        await Task.WhenAll(subAssembliesRead, linesRead);
        return new LinkedSections(await subAssembliesRead, await linesRead);
    }

    public Task<IReadOnlyList<SubAssemblyPackagingLine>> ReadPackagingSetLinesAsync(int setId, CancellationToken cancellationToken) =>
        subAssemblyRepository.GetPackagingLinesAsync(setId, cancellationToken);

    public Task<IReadOnlyList<PackagingLine>> ReadEffectivePackagingAsync(int productId, CancellationToken cancellationToken) =>
        packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);

    public async Task<SectionUsers> ReadSectionUsersAsync(CancellationToken cancellationToken)
    {
        var linkedBy = await subAssemblyRepository.GetStandaloneLinkedByAsync(cancellationToken);
        var madeFrom = await subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(cancellationToken);
        return new SectionUsers(linkedBy, madeFrom);
    }

    public async Task<ProductHistory?> ReadHistoryAsync(int productId, CancellationToken cancellationToken)
    {
        var versionsRead = Task.Run(() => versionRepository.GetForProductAsync(productId, cancellationToken), cancellationToken);
        var productRead = Task.Run(() => productRepository.GetByIdAsync(productId, cancellationToken), cancellationToken);
        var detailRead = Task.Run(() => savedProducts.ReadDetailAsync(productId, cancellationToken), cancellationToken);
        await Task.WhenAll(versionsRead, productRead, detailRead);
        return await productRead is { } product ? new ProductHistory(product, await detailRead, await versionsRead) : null;
    }
}
