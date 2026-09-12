using FluentValidation;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Validators;

public class ProductValidator : AbstractValidator<Product>
{
    public ProductValidator()
    {
        RuleFor(p => p.Name).NotEmpty().WithMessage("Name is required.");
    }
}
