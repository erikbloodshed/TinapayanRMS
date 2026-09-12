using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class PackagingCategoryValidator : AbstractValidator<PackagingCategory>
{
    public PackagingCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
    }
}
