using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly pre-check for the Alternative Units form; IngredientUnitRepository enforces the same rules itself.</summary>
public class IngredientUnitValidator : AbstractValidator<IngredientUnit>
{
    public IngredientUnitValidator()
    {
        RuleFor(u => u.UnitOfMeasureId).GreaterThan(0).WithMessage("Unit is required.");
        RuleFor(u => u.GramsPerUnit).GreaterThan(0m).WithMessage("The multiplier (grams per unit) must be positive.");
    }
}
