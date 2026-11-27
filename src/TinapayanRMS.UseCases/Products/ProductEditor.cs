using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.UseCases.Products;

/// <summary>
/// What can be done to a product: checked, saved with its recipe, packaging and costing (with its versions, all or nothing),
/// deleted to the Deleted folder, restored, deleted for good, cloned or made a variant of, given Settings' costing, and a
/// version's note set. A refusal is a <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface IProductEditor
{
    /// <summary>Refuses a product its fields can't be saved as (the product's validator).</summary>
    void EnsureValid(Product product);

    /// <summary>Refuses a costing its percentages can't be saved as (the costing's validator).</summary>
    void EnsureValid(CostingParameters costing);

    /// <summary>
    /// Saves the product and what the form changed with it, with its versions, all or nothing (VersionedSaves); the product
    /// as saved and its detail, read back. Refused as <see cref="EnsureValid(Product)"/> and
    /// <see cref="EnsureValid(CostingParameters)"/> refuse, and over a save made since the form took it.
    /// </summary>
    Task<ProductSaved> SaveAsync(ProductSaveRequest request, DateTime savedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a product to the Deleted folder, where it can be restored as it is. Refused while another product links its
    /// packaging, or while a composite holds it.
    /// </summary>
    Task DeleteAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Takes a product out of the Deleted folder, back to its own.</summary>
    Task RestoreAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Deletes a product for good, with its own sub-assemblies; the ones it links stay.</summary>
    Task DeletePermanentlyAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a product named <paramref name="name"/> from <paramref name="sourceProductId"/> as saved: a clone, its own copy
    /// of everything, or a variant, linking its recipe and packaging; its id. Its first version says where it came from
    /// (<see cref="ProductEditor.ClonedFrom"/>, <see cref="ProductEditor.VariantOf"/>).
    /// </summary>
    Task<int> MakeFromAsync(int sourceProductId, string name, bool isVariant, DateTime savedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Gives each product in <paramref name="costings"/> the costing given for it (its category's costing defaults, as the
    /// Products list worked them out for its question), each it changes with a version; their names.
    /// </summary>
    Task<IReadOnlyList<string>> ApplyCostingAsync(
        IReadOnlyList<(int ProductId, CostingParameters Costing)> costings, DateTime savedAt, CancellationToken cancellationToken);

    /// <summary>Gives a version a new note, or takes it away (null or blank). Refused when it's too long.</summary>
    Task SetVersionNoteAsync(int versionId, string? note, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IProductEditor"/>
public sealed class ProductEditor(
    IProductRepository productRepository,
    IProductVersionRepository versionRepository,
    IProductCloner cloner,
    VersionedSaves versionedSaves,
    IValidator<Product> validator,
    IValidator<CostingParameters> costingValidator) : IProductEditor
{
    /// <summary>How a clone's first version names where it came from: "Cloned from Pandesal, version 4".</summary>
    public const string ClonedFrom = "Cloned from";

    /// <summary>How a variant's first version names where it came from: "Variant of Pandesal, version 4".</summary>
    public const string VariantOf = "Variant of";

    public void EnsureValid(Product product) => Ensure(validator.Validate(product));

    public void EnsureValid(CostingParameters costing) => Ensure(costingValidator.Validate(costing));

    public async Task<ProductSaved> SaveAsync(ProductSaveRequest request, DateTime savedAt, CancellationToken cancellationToken)
    {
        EnsureValid(request.Product);
        EnsureValid(request.Costing);
        return await versionedSaves.SaveProductAsync(
            request.Product,
            request.Recipe,
            request.PackagingLink,
            request.PackagingLines,
            request.BulkPackaging,
            request.CostingChanged ? request.Costing : null,
            request.ChangesSharing,
            savedAt,
            cancellationToken);
    }

    public Task DeleteAsync(int productId, CancellationToken cancellationToken) => productRepository.DeleteAsync(productId, cancellationToken);

    public Task RestoreAsync(int productId, CancellationToken cancellationToken) => productRepository.RestoreAsync(productId, cancellationToken);

    public Task DeletePermanentlyAsync(int productId, CancellationToken cancellationToken) =>
        productRepository.DeletePermanentlyAsync(productId, cancellationToken);

    public Task<int> MakeFromAsync(int sourceProductId, string name, bool isVariant, DateTime savedAt, CancellationToken cancellationToken) =>
        versionedSaves.MakeProductFromAsync(
            sourceProductId,
            name,
            isVariant ? cloner.CreateVariantAsync : cloner.CloneAsync,
            isVariant ? VariantOf : ClonedFrom,
            savedAt,
            cancellationToken);

    public Task<IReadOnlyList<string>> ApplyCostingAsync(
        IReadOnlyList<(int ProductId, CostingParameters Costing)> costings, DateTime savedAt, CancellationToken cancellationToken) =>
        versionedSaves.ApplyCostingAsync(costings, savedAt, cancellationToken);

    public Task SetVersionNoteAsync(int versionId, string? note, CancellationToken cancellationToken) =>
        versionRepository.SetNoteAsync(versionId, note, cancellationToken);

    private static void Ensure(FluentValidation.Results.ValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
