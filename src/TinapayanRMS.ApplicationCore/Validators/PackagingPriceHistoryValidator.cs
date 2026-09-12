using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly, UI-facing pre-check mirroring PriceHistoryRules.EnsureValidPrice.</summary>
public class PackagingPriceHistoryValidator : AbstractValidator<PackagingPriceHistory>
{
    public PackagingPriceHistoryValidator()
    {
        RuleFor(h => h.Price).GreaterThan(0m).WithMessage("Price must be positive.");
    }
}
