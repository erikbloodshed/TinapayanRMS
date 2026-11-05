using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

/// <summary>
/// The Products screen's pre-check before a Save: the name and one-unit rules ProductRepository runs (RuleValidation), and
/// the checks only the form needs.
/// </summary>
public sealed class ProductValidator : AbstractValidator<Product>
{
    /// <summary>The longest description the Products table holds (ProductConfiguration).</summary>
    public const int MaxDescriptionLength = 500;

    public ProductValidator()
    {
        RuleFor(p => p.Name).Satisfies(name => ProductRules.StoredName(name ?? string.Empty));
        RuleFor(p => p.Description)
            .MaximumLength(MaxDescriptionLength)
            .WithMessage($"The description can be at most {MaxDescriptionLength} characters.");
        RuleFor(p => p.QuantityRequired)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A product has to hold at least one piece. Enter 1 for one sold by the piece.");
        RuleFor(p => p).Satisfies(p => CompositeProductRules.EnsureOneUnit(p.IsComposite, p.QuantityRequired));
        RuleFor(p => p.CurrentPrice)
            .GreaterThanOrEqualTo(0m)
            .When(p => p.CurrentPrice is not null)
            .WithMessage("The current price can't be negative.");
    }
}
