using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly, UI-facing pre-check mirroring CostingRules.EnsureValidParameters.</summary>
public sealed class CostingParametersValidator : AbstractValidator<CostingParameters>
{
    public CostingParametersValidator()
    {
        RuleFor(c => c.LaborPercent).GreaterThanOrEqualTo(0m).WithMessage("Labor percent can't be negative.");
        RuleFor(c => c.OverheadPercent).GreaterThanOrEqualTo(0m).WithMessage("Overhead percent can't be negative.");
        RuleFor(c => c.AdministrativePercent).GreaterThanOrEqualTo(0m).WithMessage("Administrative percent can't be negative.");
        RuleFor(c => c.MarginPercent).GreaterThanOrEqualTo(0m).WithMessage("Margin percent can't be negative.");
    }
}
