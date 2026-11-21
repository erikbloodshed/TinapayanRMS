using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.RawMaterials;

/// <summary>
/// What can be done to a raw material: checked, saved with its units, brand prices or add-ins (the cost cascade runs in the
/// save, IngredientCostPropagator), deleted, renamed, moved to another category, and a price history entry deleted. A refusal
/// is a <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface IRawMaterialEditor
{
    /// <summary>Refuses a material its fields can't be saved as (the material's validator).</summary>
    void EnsureValid(RawMaterial material);

    /// <summary>Refuses an alternative unit its fields can't be saved as, as the form checks each one of its table.</summary>
    void EnsureValid(RawMaterialUnit unit);

    /// <summary>Refuses a brand price its fields can't be saved as, as the form checks each one of its table.</summary>
    void EnsureValid(RawMaterialBrandPrice brandPrice);

    /// <summary>
    /// Saves the material with its units, brand prices or add-ins, all or nothing, and cascades its cost to the made and
    /// linked ingredients that use it; its id. Refused as <see cref="EnsureValid(RawMaterial)"/> refuses, and over a save
    /// made since the form took it.
    /// </summary>
    Task<int> SaveAsync(RawMaterialSaveRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int rawMaterialId, CancellationToken cancellationToken);

    /// <summary>Gives materials other categories, refused over a save made since <paramref name="checks"/> were taken.</summary>
    Task MoveAsync(IReadOnlyCollection<RawMaterialMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Renames a material, refused over a save made since <paramref name="checks"/> were taken.</summary>
    Task RenameAsync(int rawMaterialId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Deletes a price history entry, which is only a log: the material's cost stays as it is.</summary>
    Task DeletePriceHistoryAsync(int entryId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts each material's target buffer back on top of the brand it's costed at (Reports' Re-apply Target Buffer), with the
    /// cascade a Save runs, in one transaction; a material with no price, not purchased or at its target already is passed
    /// over. How many were changed.
    /// </summary>
    Task<int> ReapplyTargetBuffersAsync(IReadOnlyCollection<int> rawMaterialIds, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IRawMaterialEditor"/>
public sealed class RawMaterialEditor(
    IRawMaterialRepository rawMaterialRepository,
    IRawMaterialPriceHistoryRepository priceHistoryRepository,
    IValidator<RawMaterial> validator,
    IValidator<RawMaterialUnit> unitValidator,
    IValidator<RawMaterialBrandPrice> brandPriceValidator) : IRawMaterialEditor
{
    public void EnsureValid(RawMaterial material) => Ensure(validator.Validate(material));

    public void EnsureValid(RawMaterialUnit unit) => Ensure(unitValidator.Validate(unit));

    public void EnsureValid(RawMaterialBrandPrice brandPrice) => Ensure(brandPriceValidator.Validate(brandPrice));

    public async Task<int> SaveAsync(RawMaterialSaveRequest request, CancellationToken cancellationToken)
    {
        EnsureValid(request.Material);
        return await rawMaterialRepository.SaveAsync(request.Material, request.Units, request.BrandPrices, request.AddIns, cancellationToken);
    }

    public Task DeleteAsync(int rawMaterialId, CancellationToken cancellationToken) => rawMaterialRepository.DeleteAsync(rawMaterialId, cancellationToken);

    public Task MoveAsync(IReadOnlyCollection<RawMaterialMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        rawMaterialRepository.MoveAsync(moves, checks, cancellationToken);

    public Task RenameAsync(int rawMaterialId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        rawMaterialRepository.RenameAsync(rawMaterialId, name, checks, cancellationToken);

    public Task DeletePriceHistoryAsync(int entryId, CancellationToken cancellationToken) => priceHistoryRepository.DeleteAsync(entryId, cancellationToken);

    public Task<int> ReapplyTargetBuffersAsync(IReadOnlyCollection<int> rawMaterialIds, CancellationToken cancellationToken) =>
        rawMaterialRepository.ReapplyTargetBuffersAsync(rawMaterialIds, cancellationToken);

    private static void Ensure(FluentValidation.Results.ValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
