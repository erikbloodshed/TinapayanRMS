using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public sealed class SubAssemblyValidator : AbstractValidator<SubAssembly>
{
    public SubAssemblyValidator()
    {
        RuleFor(s => s.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(s => s.RoleId).GreaterThan(0).WithMessage("Role is required.");
    }
}
