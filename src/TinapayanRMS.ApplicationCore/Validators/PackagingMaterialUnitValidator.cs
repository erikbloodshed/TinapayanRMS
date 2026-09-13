using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly pre-check for the packaging Alternative Units form; PackagingMaterialUnitRepository enforces the same rules itself.</summary>
public class PackagingMaterialUnitValidator : AbstractValidator<PackagingMaterialUnit>
{
    public PackagingMaterialUnitValidator()
    {
        RuleFor(u => u.UnitOfMeasureId).GreaterThan(0).WithMessage("Unit is required.");
        RuleFor(u => u.BaseUnitsPerUnit).GreaterThan(0m).WithMessage("The multiplier (base units per unit) must be positive.");
    }
}
