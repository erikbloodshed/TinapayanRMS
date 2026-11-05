using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// The Purchase Cost Setup grid's pre-check, run when the Raw Materials screen saves: the price rule
/// RawMaterialBrandPriceRepository runs (RuleValidation), and the brand's length, which its column holds.
/// </summary>
public sealed class RawMaterialBrandPriceValidator : AbstractValidator<RawMaterialBrandPrice>
{
    public const int MaxBrandLength = 100;

    public RawMaterialBrandPriceValidator()
    {
        RuleFor(p => p.Brand).MaximumLength(MaxBrandLength).WithMessage($"A brand is at most {MaxBrandLength} characters.");
        RuleFor(p => p.Price).Satisfies(RawMaterialRules.EnsureValidBrandPrice);
    }
}
