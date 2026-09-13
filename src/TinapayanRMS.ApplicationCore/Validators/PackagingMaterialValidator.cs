using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class PackagingMaterialValidator : AbstractValidator<PackagingMaterial>
{
    public PackagingMaterialValidator()
    {
        RuleFor(m => m.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(m => m.PackagingCategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(m => m.BaseUnitId).GreaterThan(0).WithMessage("Base unit is required.");
    }
}
