using FluentValidation;
using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore.Validators;

public class PackagingMaterialValidator : AbstractValidator<PackagingMaterial>
{
    public PackagingMaterialValidator()
    {
        RuleFor(m => m.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(m => m.PackagingCategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(m => m.StandardUnitId).GreaterThan(0).WithMessage("Standard unit is required.");
        RuleFor(m => m.PurchaseUnitId).GreaterThan(0).WithMessage("Purchase unit is required.");
        RuleFor(m => m.PurchaseUnitQty).GreaterThan(0m).WithMessage("Purchase unit quantity must be positive.");
    }
}
