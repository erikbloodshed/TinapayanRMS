using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Pricing.Tests;

public class TriedPriceChangeTests
{
    /// <summary>Flour bought by the 25 kg sack at 1,250: 0.05 a gram, 0.055 buffered.</summary>
    private static readonly RawMaterial Flour = new() { Id = 1, Name = "Flour", CostPerBaseUnit = 0.05m, BufferedCostPerBaseUnit = 0.055m, CostBufferPercent = 10m };

    private static readonly PurchaseUnit Sack = new("sack", 25_000m, 1_250m);

    [Fact]
    public void A_change_in_percent_works_out_the_new_price_and_moves_both_costs_by_it()
    {
        var tried = TriedPriceChange.Of(Flour, Sack, changePercent: 10m, newPrice: null, percentTyped: true)!;

        (tried.NewPrice, tried.ChangePercent, tried.Cost, tried.BufferedCost).Should().Be((1_375m, (decimal?)10m, 0.055m, 0.0605m));
    }

    [Fact]
    public void A_new_price_works_out_the_change_in_percent()
    {
        var tried = TriedPriceChange.Of(Flour, Sack, changePercent: null, newPrice: 1_000m, percentTyped: false)!;

        (tried.NewPrice, tried.ChangePercent, tried.Cost, tried.BufferedCost).Should().Be((1_000m, (decimal?)-20m, 0.04m, 0.044m));
    }

    [Fact]
    public void A_material_with_no_price_yet_is_costed_from_the_new_price_with_its_buffer_on_top()
    {
        var tried = TriedPriceChange.Of(Flour, Sack with { Price = null }, changePercent: null, newPrice: 500m, percentTyped: false)!;

        (tried.ChangePercent, tried.Cost, tried.BufferedCost).Should().Be(((decimal?)null, 0.02m, 0.022m));
    }

    [Fact]
    public void Nothing_typed_yet_tries_nothing()
    {
        TriedPriceChange.Of(Flour, Sack, changePercent: null, newPrice: null, percentTyped: true).Should().BeNull();
        TriedPriceChange.Of(Flour, Sack, changePercent: null, newPrice: null, percentTyped: false).Should().BeNull();
    }

    [Fact]
    public void A_percentage_on_a_material_with_no_price_a_fall_of_100_percent_and_a_price_of_0_are_refused()
    {
        var noPrice = () => TriedPriceChange.Of(Flour, Sack with { Price = null }, 10m, null, percentTyped: true);
        var tooFar = () => TriedPriceChange.Of(Flour, Sack, -100m, null, percentTyped: true);
        var free = () => TriedPriceChange.Of(Flour, Sack, null, 0m, percentTyped: false);

        noPrice.Should().Throw<BusinessRuleException>().WithMessage($"Flour: {PriceChangeRules.NoPriceToChangeText}");
        tooFar.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.FallTooFarText);
        free.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.NewPriceTooLowText);
    }
}
