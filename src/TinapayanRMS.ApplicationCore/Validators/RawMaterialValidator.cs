using FluentValidation;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// Friendly, UI-facing pre-check mirroring RawMaterialRules.EnsureFieldsMatchType and
/// EnsureFieldsMatchSource. This is a courtesy layer only — RawMaterialRepository still runs both
/// rules itself before every save, so a caller that skips this validator (or a bug in it) can never
/// leave the database in a state the domain rules wouldn't have allowed anyway.
/// </summary>
public class RawMaterialValidator : AbstractValidator<RawMaterial>
{
    public RawMaterialValidator()
    {
        RuleFor(m => m.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(m => m.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(m => m.CostBufferPercent)
            .GreaterThanOrEqualTo(0m).LessThanOrEqualTo(100m).WithMessage("Cost buffer must be at least 0 and at most 100 percent.");

        When(m => m.Type == RawMaterialType.Ingredient, () =>
        {
            RuleFor(m => m.BaseUnitId).Null().WithMessage("An ingredient's base unit is always the gram.");
        });

        When(m => m.Type == RawMaterialType.Packaging, () =>
        {
            RuleFor(m => m.BaseUnitId)
                .NotNull().WithMessage("Base unit is required.")
                .GreaterThan(0).WithMessage("Base unit is required.");
            RuleFor(m => m.Source).Equal(RawMaterialSource.Purchased).WithMessage("Only an ingredient can be made or linked; packaging is always purchased.");
        });

        When(m => m.Source == RawMaterialSource.Purchased, () =>
        {
            RuleFor(m => m.WeightLossPercent).Null().WithMessage("A purchased material has no weight loss percent.");
        });

        When(m => m.Source != RawMaterialSource.Linked, () =>
        {
            RuleFor(m => m.LinkedToId).Null().WithMessage("Only a linked ingredient is linked to another.");
            RuleFor(m => m.WeightSharePercent).Null().WithMessage("Only a linked ingredient is linked to another.");
            RuleFor(m => m.CostSharePercent).Null().WithMessage("Only a linked ingredient is linked to another.");
        });

        When(m => m.Source == RawMaterialSource.Linked, () =>
        {
            RuleFor(m => m.WeightLossPercent).Null().WithMessage("A linked ingredient has no weight loss percent.");
            RuleFor(m => m.LinkedToId)
                .NotNull().WithMessage("Pick the ingredient it's linked to.")
                .GreaterThan(0).WithMessage("Pick the ingredient it's linked to.");
            RuleFor(m => m.WeightSharePercent)
                .NotNull().WithMessage("Weight share is required.")
                .GreaterThan(0m).LessThanOrEqualTo(100m).WithMessage("Weight share must be above 0 and at most 100 percent.");
            RuleFor(m => m.CostSharePercent)
                .NotNull().WithMessage("Cost share is required.")
                .GreaterThan(0m).LessThanOrEqualTo(100m).WithMessage("Cost share must be above 0 and at most 100 percent.");
        });

        When(m => m.Source == RawMaterialSource.Made, () =>
        {
            RuleFor(m => m.WeightLossPercent)
                .NotNull().WithMessage("A made ingredient needs a weight loss percent.");
            RuleFor(m => m.WeightLossPercent!.Value)
                .GreaterThanOrEqualTo(0m).LessThan(100m).WithMessage("Weight loss percent must be at least 0 and less than 100.")
                .When(m => m.WeightLossPercent is not null);
        });
    }
}
