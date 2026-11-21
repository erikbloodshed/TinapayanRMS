using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// The Products screen's and Settings' pre-check of the costing percentages: CostingRules.EnsureValidParameters, which the
/// repositories run as they save (RuleValidation).
/// </summary>
public sealed class CostingParametersValidator : AbstractValidator<CostingParameters>
{
    public CostingParametersValidator()
    {
        RuleFor(c => c).Satisfies(c => CostingRules.EnsureValidParameters(c.LaborPercent, c.OverheadPercent, c.AdministrativePercent, c.MarkupPercent));
    }
}
