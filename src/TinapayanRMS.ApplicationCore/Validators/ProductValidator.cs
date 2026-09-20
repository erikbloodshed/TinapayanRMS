using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class ProductValidator : AbstractValidator<Product>
{
    /// <summary>The longest description the Products table holds (ProductConfiguration).</summary>
    public const int MaxDescriptionLength = 500;

    public ProductValidator()
    {
        RuleFor(p => p.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(p => p.Description)
            .MaximumLength(MaxDescriptionLength)
            .WithMessage($"The description can be at most {MaxDescriptionLength} characters.");
        RuleFor(p => p.QuantityRequired)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A product has to hold at least one piece. Enter 1 for one sold by the piece.");
        RuleFor(p => p.CurrentPrice)
            .GreaterThanOrEqualTo(0m)
            .When(p => p.CurrentPrice is not null)
            .WithMessage("The current price can't be negative.");
    }
}
