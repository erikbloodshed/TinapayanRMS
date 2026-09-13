using FluentValidation;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// Friendly, UI-facing pre-check mirroring IngredientRules.EnsureFieldsMatchSource. This
/// is a courtesy layer only — IngredientRepository still runs EnsureFieldsMatchSource
/// itself before every save, so a caller that skips this validator (or a bug in it) can
/// never leave the database in a state the domain rule wouldn't have allowed anyway.
/// </summary>
public class IngredientValidator : AbstractValidator<Ingredient>
{
    public IngredientValidator()
    {
        RuleFor(i => i.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(i => i.IngredientCategoryId).GreaterThan(0).WithMessage("Category is required.");

        When(i => i.Source == IngredientSource.Purchased, () =>
        {
            RuleFor(i => i.WeightLossPercent).Null().WithMessage("A purchased ingredient has no weight loss percent.");
        });

        When(i => i.Source == IngredientSource.Made, () =>
        {
            RuleFor(i => i.WeightLossPercent)
                .NotNull().WithMessage("A made ingredient needs a weight loss percent.");
            RuleFor(i => i.WeightLossPercent!.Value)
                .GreaterThanOrEqualTo(0m).LessThan(100m).WithMessage("Weight loss percent must be at least 0 and less than 100.")
                .When(i => i.WeightLossPercent is not null);
        });
    }
}
