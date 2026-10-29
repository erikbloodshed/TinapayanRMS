using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public sealed class ProductCategoryValidator : AbstractValidator<ProductCategory>
{
    public ProductCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
    }
}
