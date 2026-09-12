using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests.Validators;

public class PriceHistoryValidatorTests
{
    [Fact]
    public void IngredientPriceHistoryValidator_accepts_a_positive_price()
    {
        new IngredientPriceHistoryValidator().Validate(new IngredientPriceHistory { Price = 90m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void IngredientPriceHistoryValidator_rejects_a_non_positive_price()
    {
        new IngredientPriceHistoryValidator().Validate(new IngredientPriceHistory { Price = 0m }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PackagingPriceHistoryValidator_accepts_a_positive_price()
    {
        new PackagingPriceHistoryValidator().Validate(new PackagingPriceHistory { Price = 45m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PackagingPriceHistoryValidator_rejects_a_non_positive_price()
    {
        new PackagingPriceHistoryValidator().Validate(new PackagingPriceHistory { Price = -5m }).IsValid.Should().BeFalse();
    }
}
