using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public sealed class UnitOfMeasureValidator : AbstractValidator<UnitOfMeasure>
{
    public UnitOfMeasureValidator()
    {
        // Casing is auto-corrected (UnitOfMeasureRules.NormalizeName), not rejected, so
        // there's no "must be lowercase" rule here -- only that a name is present at all.
        RuleFor(u => u.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(u => u.Abbreviation).NotEmpty().WithMessage("Abbreviation is required.");
    }
}
