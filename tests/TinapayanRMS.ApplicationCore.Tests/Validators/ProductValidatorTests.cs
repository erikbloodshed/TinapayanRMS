using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class ProductValidatorTests
{
    private readonly ProductValidator _validator = new();

    [Test]
    public void Accepts_a_product_with_a_name()
    {
        _validator.Validate(new Product { Name = "Ensaymada" }).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_missing_name()
    {
        _validator.Validate(new Product { Name = "" }).IsValid.Should().BeFalse();
    }

    [Test]
    public void A_new_product_holds_one_piece()
    {
        new Product { Name = "Ensaymada" }.QuantityRequired.Should().Be(1);
    }

    [Test]
    public void Accepts_a_quantity_required_of_more_than_one_piece()
    {
        _validator.Validate(new Product { Name = "Ensaymada Box of 8", QuantityRequired = 8 }).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_quantity_required_below_one()
    {
        _validator.Validate(new Product { Name = "Ensaymada", QuantityRequired = 0 }).IsValid.Should().BeFalse();
    }

    [Test]
    public void A_composite_product_is_one_sellable_unit()
    {
        _validator.Validate(new Product { Name = "Pasalubong Box", IsComposite = true }).IsValid.Should().BeTrue();
        _validator.Validate(new Product { Name = "Pasalubong Box", IsComposite = true, QuantityRequired = 2 }).IsValid.Should().BeFalse();
    }

    [Test]
    public void Accepts_a_description_at_the_limit()
    {
        var product = new Product { Name = "Ensaymada", Description = new string('a', ProductValidator.MaxDescriptionLength) };

        _validator.Validate(product).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_description_over_the_limit()
    {
        var product = new Product { Name = "Ensaymada", Description = new string('a', ProductValidator.MaxDescriptionLength + 1) };

        _validator.Validate(product).IsValid.Should().BeFalse();
    }

    public static IEnumerable<decimal?> CurrentPrices() => [(decimal?)null, 0m, 25.5m];

    [Test]
    [MethodDataSource(nameof(CurrentPrices))]
    public void Accepts_no_current_price_or_one_of_zero_or_more(decimal? currentPrice)
    {
        var product = new Product { Name = "Ensaymada", CurrentPrice = currentPrice };

        _validator.Validate(product).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_negative_current_price()
    {
        _validator.Validate(new Product { Name = "Ensaymada", CurrentPrice = -0.01m }).IsValid.Should().BeFalse();
    }
}
