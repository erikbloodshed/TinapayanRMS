using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly pre-check for the Purchase Cost Setup grid, run when the Raw Materials screen saves; RawMaterialBrandPriceRepository enforces the same rules itself.</summary>
public sealed class RawMaterialBrandPriceValidator : AbstractValidator<RawMaterialBrandPrice>
{
    public const int MaxBrandLength = 100;

    public RawMaterialBrandPriceValidator()
    {
        RuleFor(p => p.Brand).MaximumLength(MaxBrandLength).WithMessage($"A brand is at most {MaxBrandLength} characters.");
        RuleFor(p => p.Price).GreaterThan(0m).WithMessage("Purchase price must be positive.");
    }
}
