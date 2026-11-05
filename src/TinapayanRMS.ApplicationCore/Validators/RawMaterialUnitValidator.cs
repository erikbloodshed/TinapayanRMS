using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// The Alternative Units grid's pre-check, run when the Raw Materials screen saves: a unit picked, and the multiplier rule
/// RawMaterialUnitRepository runs (RuleValidation).
/// </summary>
public sealed class RawMaterialUnitValidator : AbstractValidator<RawMaterialUnit>
{
    public RawMaterialUnitValidator()
    {
        RuleFor(u => u.UnitOfMeasureId).GreaterThan(0).WithMessage("Unit is required.");
        RuleFor(u => u.BaseUnitsPerUnit).Satisfies(UnitConversionRules.EnsureValidMultiplier);
    }
}
