using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// Everything the Products screen's LoadCommand reads, as <see cref="ProductsLoadReader.ReadAsync"/> reads it, for the
/// screen to merge in (ProductsState.LoadAsync).
/// </summary>
/// <param name="PackagingSets">The packaging sets: the standalone sub-assemblies whose role is a packaging one.</param>
/// <param name="LinkedSubAssemblies">The sub-assemblies the form's saved sections are, as they're saved now.</param>
/// <param name="LinkedLines">Those sub-assemblies' lines.</param>
/// <param name="PackagingLink">The product showing's packaging link as saved, or null: none, or no product showing.</param>
/// <param name="ComponentLinks">Every composite's products, which the Products table's lines point at.</param>
/// <param name="ComponentCosts">What each product the form's composite holds brings, by id.</param>
public sealed record ProductsLoad(
    IReadOnlyList<SubAssemblyRole> Roles,
    IReadOnlyList<ProductCategory> Categories,
    IReadOnlyList<ProductFolder> Folders,
    IReadOnlyList<RawMaterial> RawMaterials,
    IReadOnlyList<Product> Products,
    IReadOnlyList<Product> DeletedProducts,
    IReadOnlyList<SubAssembly> Linkable,
    IReadOnlyList<SubAssembly> PackagingSets,
    CostingDefaults CostingDefaults,
    IReadOnlyList<SubAssembly> LinkedSubAssemblies,
    IReadOnlyList<SubAssemblyLine> LinkedLines,
    PackagingLink? PackagingLink,
    IReadOnlyList<ProductComponent> ComponentLinks,
    IReadOnlyDictionary<int, ComponentCosts> ComponentCosts);

/// <summary>
/// The Products screen's reads for its LoadCommand, apart from the merge into the screen that follows them: the lists the
/// form and its parts pick from, the products and their folders, Settings' defaults, and what the form shows from elsewhere
/// (its linked sections, its packaging link, a composite's products' costs). It touches no screen state, as
/// SavedProductReader doesn't for a product's detail.
/// </summary>
internal sealed class ProductsLoadReader(
    IProductRepository productRepository,
    ISubAssemblyRoleRepository subAssemblyRoleRepository,
    IProductCategoryRepository categoryRepository,
    IProductFolderRepository folderRepository,
    IRawMaterialRepository rawMaterialRepository,
    ISubAssemblyRepository subAssemblyRepository,
    ISubAssemblyLineRepository subAssemblyLineRepository,
    ICostingDefaultsRepository costingDefaultsRepository,
    IPackagingLinkRepository packagingLinkRepository,
    IPackagingLineRepository packagingLineRepository,
    SavedProductReader savedProducts)
{
    /// <summary>
    /// Everything a load reads: the sub-assemblies <paramref name="linkedIds"/> and their lines, the packaging link of
    /// <paramref name="detailProductId"/> (the product showing, or null for none) and what each of
    /// <paramref name="componentIds"/> brings to a composite, with the lists every load reads.
    /// </summary>
    public async Task<ProductsLoad> ReadAsync(int? detailProductId, IReadOnlyCollection<int> linkedIds, IReadOnlyCollection<int> componentIds)
    {
        // All at once, on the thread pool: each read is mostly a round trip's wait, so one after another they took
        // several times as long as the slowest of them, and the UI thread came back after each to start the next.
        // Each has its own context (AddDbContextFactory).
        var rolesRead = Task.Run(() => subAssemblyRoleRepository.GetAllAsync(CancellationToken.None));
        var categoriesRead = Task.Run(() => categoryRepository.GetAllAsync(CancellationToken.None));
        var foldersRead = Task.Run(() => folderRepository.GetAllAsync(CancellationToken.None));
        var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(CancellationToken.None));
        var productsRead = Task.Run(() => productRepository.GetAllAsync(CancellationToken.None));
        var deletedRead = Task.Run(() => productRepository.GetDeletedAsync(CancellationToken.None));
        var linkableRead = Task.Run(() => subAssemblyRepository.GetLinkableAsync(CancellationToken.None));
        var standaloneRead = Task.Run(() => subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
        var costingDefaultsRead = Task.Run(() => costingDefaultsRepository.GetAsync(CancellationToken.None));
        var linkedSubAssembliesRead = Task.Run(() => subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None));
        var linkedLinesRead = Task.Run(() => subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None));
        var packagingLinkRead = detailProductId is { } linkProductId
            ? Task.Run(() => packagingLinkRepository.GetForProductAsync(linkProductId, CancellationToken.None))
            : Task.FromResult<PackagingLink?>(null);
        var componentLinksRead = Task.Run(() => productRepository.GetAllComponentsAsync(CancellationToken.None));
        var componentCostsRead = Task.Run(() => savedProducts.ReadComponentCostsAsync(componentIds, CancellationToken.None));
        await Task.WhenAll(
            rolesRead, categoriesRead, foldersRead, rawMaterialsRead, productsRead, deletedRead, linkableRead, standaloneRead, costingDefaultsRead,
            linkedSubAssembliesRead, linkedLinesRead, packagingLinkRead, componentLinksRead, componentCostsRead);

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
            PackagingLink: await packagingLinkRead,
            ComponentLinks: await componentLinksRead,
            ComponentCosts: await componentCostsRead);
    }

    /// <summary>
    /// The packaging lines <paramref name="productId"/> shows for one sellable unit as they're saved now: through its link,
    /// the linked set's or product's (IPackagingLineRepository.GetEffectiveForProductAsync).
    /// </summary>
    public Task<IReadOnlyList<PackagingLine>> ReadLinkedPackagingAsync(int productId) =>
        packagingLineRepository.GetEffectiveForProductAsync(productId, CancellationToken.None);
}
