using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class BrandPriceValidatorTests
{
    private readonly RawMaterialBrandPriceValidator _validator = new();

    [Theory]
    [InlineData(90)]
    [InlineData(1)]
    public void Accepts_a_positive_price(decimal price)
    {
        _validator.Validate(new RawMaterialBrandPrice { Price = price }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_a_non_positive_price(decimal price)
    {
        _validator.Validate(new RawMaterialBrandPrice { Price = price }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_brand_past_the_column_length()
    {
        var brand = new string('x', RawMaterialBrandPriceValidator.MaxBrandLength + 1);

        _validator.Validate(new RawMaterialBrandPrice { Brand = brand, Price = 1m }).IsValid.Should().BeFalse();
    }
}
