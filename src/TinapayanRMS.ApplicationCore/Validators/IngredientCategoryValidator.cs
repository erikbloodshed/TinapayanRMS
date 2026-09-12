using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class IngredientCategoryValidator : AbstractValidator<IngredientCategory>
{
    public IngredientCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.");
    }
}
