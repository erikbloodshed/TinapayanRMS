using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.Products;

/// <summary>
/// Everything the Products view's LoadCommand reads, as <see cref="IProductQueries.ReadLoadAsync"/> reads it, for the view
/// to merge into its lists and every open form (ProductsState.LoadAsync).
/// </summary>
/// <param name="PackagingSets">The packaging sets: the standalone sub-assemblies whose role is a packaging one.</param>
/// <param name="LinkedSubAssemblies">The sub-assemblies the open forms' saved sections are, as they're saved now.</param>
/// <param name="LinkedLines">Those sub-assemblies' lines.</param>
/// <param name="PackagingLinks">The open products' packaging links as saved, by product; one with none isn't there.</param>
/// <param name="ComponentLinks">Every composite's products, which the Products table's lines point at.</param>
/// <param name="ComponentCosts">What each product the open composites hold brings, by id.</param>
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
    IReadOnlyDictionary<int, PackagingLink> PackagingLinks,
    IReadOnlyList<ProductComponent> ComponentLinks,
    IReadOnlyDictionary<int, ComponentCosts> ComponentCosts);

/// <summary>A product's versions for its History tab, with the product as it's saved now, which each is compared with.</summary>
public sealed record ProductHistory(Product Product, ProductDetail Detail, IReadOnlyList<ProductVersion> Versions);

/// <summary>Sub-assemblies as they're saved now, with their lines.</summary>
public sealed record LinkedSections(IReadOnlyList<SubAssembly> SubAssemblies, IReadOnlyList<SubAssemblyLine> Lines);

/// <summary>
/// Who else uses the standalone sub-assemblies, by sub-assembly: the products linking each, and the made ingredients made
/// from each. What a Save changing a linked section's lines asks about.
/// </summary>
public sealed record SectionUsers(
    IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> LinkedBy,
    IReadOnlyDictionary<int, IReadOnlyList<MadeIngredientUse>> MadeFrom);

/// <summary>
/// A product's Save: the product, and what of the rest the form changed (null where it didn't), written all or nothing with
/// its versions (VersionedSaves.SaveProductAsync).
/// </summary>
/// <param name="Costing">The product's costing, checked as the product is; saved only when <paramref name="CostingChanged"/>.</param>
/// <param name="ChangesSharing">
/// Whether the Save reaches the products sharing its sections' lines or linking its packaging, which then each get a version.
/// </param>
public sealed record ProductSaveRequest(
    Product Product,
    CostingParameters Costing,
    bool CostingChanged,
    RecipeDraft? Recipe,
    PackagingLinkDraft? PackagingLink,
    IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? PackagingLines,
    BulkPackagingDraft? BulkPackaging,
    bool ChangesSharing);
