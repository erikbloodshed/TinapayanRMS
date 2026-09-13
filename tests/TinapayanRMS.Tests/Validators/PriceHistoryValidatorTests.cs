using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class PriceHistoryValidatorTests
{
    private readonly RawMaterialPriceHistoryValidator _validator = new();

    [Theory]
    [InlineData(90)]
    [InlineData(1)]
    public void Accepts_a_positive_price(decimal price)
    {
        _validator.Validate(new RawMaterialPriceHistory { Price = price }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_a_non_positive_price(decimal price)
    {
        _validator.Validate(new RawMaterialPriceHistory { Price = price }).IsValid.Should().BeFalse();
    }
}
