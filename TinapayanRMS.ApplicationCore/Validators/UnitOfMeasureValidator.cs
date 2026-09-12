using FluentValidation;
using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore.Validators;

public class UnitOfMeasureValidator : AbstractValidator<UnitOfMeasure>
{
    public UnitOfMeasureValidator()
    {
        RuleFor(u => u.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(u => u.Name)
            .Must(name => name == name.ToLowerInvariant())
            .WithMessage("Name must be all lowercase.")
            .When(u => !string.IsNullOrEmpty(u.Name));
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
