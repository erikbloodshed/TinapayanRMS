using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public sealed class SubAssemblyRoleValidator : AbstractValidator<SubAssemblyRole>
{
    public SubAssemblyRoleValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(r => r).Satisfies(SubAssemblyRules.EnsureRoleTicksAllowed);
    }
}
