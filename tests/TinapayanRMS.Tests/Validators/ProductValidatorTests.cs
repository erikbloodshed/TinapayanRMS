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
    public void A_new_product_holds_one_piece()
    {
        new Product { Name = "Ensaymada" }.QuantityRequired.Should().Be(1);
    }

    [Fact]
    public void Accepts_a_quantity_required_of_more_than_one_piece()
    {
        _validator.Validate(new Product { Name = "Ensaymada Box of 8", QuantityRequired = 8 }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_quantity_required_below_one()
    {
        _validator.Validate(new Product { Name = "Ensaymada", QuantityRequired = 0 }).IsValid.Should().BeFalse();
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

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(25.5)]
    public void Accepts_no_current_price_or_one_of_zero_or_more(double? currentPrice)
    {
        var product = new Product { Name = "Ensaymada", CurrentPrice = (decimal?)currentPrice };

        _validator.Validate(product).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_negative_current_price()
    {
        _validator.Validate(new Product { Name = "Ensaymada", CurrentPrice = -0.01m }).IsValid.Should().BeFalse();
    }
}
