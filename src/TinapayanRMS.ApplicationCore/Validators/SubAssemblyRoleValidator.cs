using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class SubAssemblyRoleValidator : AbstractValidator<SubAssemblyRole>
{
    public SubAssemblyRoleValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(r => r.IsProductOwned).Equal(false).When(r => r.IsPackaging)
            .WithMessage("The Packaging role holds packaging sets from the Sub-Assemblies screen, so it can't be product-owned.");
    }
}
