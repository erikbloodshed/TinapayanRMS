using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// The Raw Materials screen's pre-check before a Save: the rules RawMaterialRepository runs on every save (its name, the
/// fields its type and source call for, its buffer and buffered cost), through RuleValidation, and the category the form
/// must have picked.
/// </summary>
public sealed class RawMaterialValidator : AbstractValidator<RawMaterial>
{
    public RawMaterialValidator()
    {
        RuleFor(m => m.Name).Satisfies(name => RawMaterialRules.StoredName(name ?? string.Empty));
        RuleFor(m => m.CategoryId).GreaterThan(0).WithMessage("Category is required.");
        RuleFor(m => m.CostBufferPercent).Satisfies(RawMaterialRules.EnsureValidCostBuffer);
        RuleFor(m => m).Satisfies(RawMaterialRules.EnsureFieldsMatchType);
        RuleFor(m => m).Satisfies(RawMaterialRules.EnsureFieldsMatchSource);
        RuleFor(m => m.BufferedCostPerBaseUnit).Satisfies(RawMaterialRules.EnsureValidBufferedCost);
    }
}
