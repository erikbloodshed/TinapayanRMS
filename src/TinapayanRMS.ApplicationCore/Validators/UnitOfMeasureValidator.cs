using FluentValidation;
using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore.Validators;

public class UnitOfMeasureValidator : AbstractValidator<UnitOfMeasure>
{
    public UnitOfMeasureValidator()
    {
        // Casing is auto-corrected (UnitOfMeasureRules.NormalizeName), not rejected, so
        // there's no "must be lowercase" rule here -- only that a name is present at all.
        RuleFor(u => u.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(u => u.Abbreviation).NotEmpty().WithMessage("Abbreviation is required.");

        RuleFor(u => u.MillimetersPerUnit)
            .Must(factor => factor is > 0)
            .WithMessage("A length unit needs a positive conversion factor (millimeters per unit).")
            .When(u => u.IsLengthUnit);
        RuleFor(u => u.MillimetersPerUnit)
            .Must(factor => factor is null)
            .WithMessage("Only a length unit has a conversion factor.")
            .When(u => !u.IsLengthUnit);
    }
}
