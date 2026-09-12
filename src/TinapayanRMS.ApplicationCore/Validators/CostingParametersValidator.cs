using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly, UI-facing pre-check mirroring CostingRules.EnsureValidParameters.</summary>
public class CostingParametersValidator : AbstractValidator<CostingParameters>
{
    public CostingParametersValidator()
    {
        RuleFor(c => c.LaborCost).GreaterThanOrEqualTo(0m).WithMessage("Labor cost can't be negative.");
        RuleFor(c => c.OverheadCost).GreaterThanOrEqualTo(0m).WithMessage("Overhead cost can't be negative.");
        RuleFor(c => c.MarginPercent).GreaterThanOrEqualTo(0m).WithMessage("Margin percent can't be negative.");
        RuleFor(c => c.VatPercent).GreaterThanOrEqualTo(0m).WithMessage("VAT percent can't be negative.");
    }
}
