using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>Friendly pre-check for the Packs table, run when the Products screen saves; ProductPackRepository enforces the same rules itself.</summary>
public class ProductPackValidator : AbstractValidator<ProductPack>
{
    /// <summary>The longest pack name the ProductPacks table holds (ProductPackConfiguration).</summary>
    public const int MaxNameLength = 60;

    public ProductPackValidator()
    {
        RuleFor(p => p.Name).NotEmpty().WithMessage("The pack's name is required.");
        RuleFor(p => p.Name)
            .MaximumLength(MaxNameLength)
            .WithMessage($"The pack's name can be at most {MaxNameLength} characters.");
        RuleFor(p => p.PiecesPerPack).GreaterThan(0).WithMessage("A pack has to hold at least one piece.");
    }
}
