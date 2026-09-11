using FluentValidation;
using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// Friendly, UI-facing pre-check. PackagingMaterialRepository still validates the
/// standard unit itself before every save (PackagingMaterialRules.EnsureValidStandardUnit),
/// so this is a courtesy layer only, not the last line of defense.
/// </summary>
public class PackagingMaterialValidator : AbstractValidator<PackagingMaterial>
{
    public PackagingMaterialValidator()
    {
        RuleFor(m => m.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(m => m.PackagingCategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(m => m.StandardUnitId)
            .Must(PackagingMaterialRules.IsValidStandardUnit)
            .WithMessage("Standard unit must be Piece or a length unit.");
        RuleFor(m => m.PurchaseUnitId).GreaterThan(0).WithMessage("Purchase unit is required.");
        RuleFor(m => m.PurchaseUnitQty).GreaterThan(0m).WithMessage("Purchase unit quantity must be positive.");
    }
}
