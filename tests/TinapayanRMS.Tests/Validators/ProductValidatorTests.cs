using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class ProductValidatorTests
{
    private readonly ProductValidator _validator = new();

    [Fact]
    public void Accepts_a_product_with_a_name()
    {
        _validator.Validate(new Product { Name = "Ensaymada" }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        _validator.Validate(new Product { Name = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_a_description_at_the_limit()
    {
        var product = new Product { Name = "Ensaymada", Description = new string('a', ProductValidator.MaxDescriptionLength) };

        _validator.Validate(product).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_description_over_the_limit()
    {
        var product = new Product { Name = "Ensaymada", Description = new string('a', ProductValidator.MaxDescriptionLength + 1) };

        _validator.Validate(product).IsValid.Should().BeFalse();
    }
}
