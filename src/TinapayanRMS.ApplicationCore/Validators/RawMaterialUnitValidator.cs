using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly pre-check for the Alternative Units grid, run when the Raw Materials screen saves; RawMaterialUnitRepository enforces the same rules itself.</summary>
public class RawMaterialUnitValidator : AbstractValidator<RawMaterialUnit>
{
    public RawMaterialUnitValidator()
    {
        RuleFor(u => u.UnitOfMeasureId).GreaterThan(0).WithMessage("Unit is required.");
        RuleFor(u => u.BaseUnitsPerUnit).GreaterThan(0m).WithMessage("The multiplier (base units per unit) must be positive.");
    }
}
