using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly, UI-facing pre-check mirroring PriceHistoryRules.EnsureValidPrice.</summary>
public class RawMaterialPriceHistoryValidator : AbstractValidator<RawMaterialPriceHistory>
{
    public RawMaterialPriceHistoryValidator()
    {
        RuleFor(h => h.Price).GreaterThan(0m).WithMessage("Price must be positive.");
    }
}
