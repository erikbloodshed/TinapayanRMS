using FluentValidation;
using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore.Validators;

public class UnitOfMeasureValidator : AbstractValidator<UnitOfMeasure>
{
    public UnitOfMeasureValidator()
    {
        RuleFor(u => u.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(u => u.Abbreviation).NotEmpty().WithMessage("Abbreviation is required.");
    }
}
