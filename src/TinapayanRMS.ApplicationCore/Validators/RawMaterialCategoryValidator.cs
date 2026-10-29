using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public sealed class RawMaterialCategoryValidator : AbstractValidator<RawMaterialCategory>
{
    public RawMaterialCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(c => c.Type).IsInEnum().WithMessage("Pick whether the category is for ingredients, made ingredients or packaging.");
    }
}
