using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class SubAssemblyRoleValidator : AbstractValidator<SubAssemblyRole>
{
    public SubAssemblyRoleValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
    }
}
