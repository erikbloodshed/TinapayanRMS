using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class PackagingMaterialRulesTests
{
    [Fact]
    public void IsValidStandardUnit_is_true_for_piece()
    {
        PackagingMaterialRules.IsValidStandardUnit(WellKnownUnitOfMeasureIds.Piece).Should().BeTrue();
    }

    [Fact]
    public void IsValidStandardUnit_is_true_for_every_length_unit()
    {
        foreach (var id in WellKnownUnitOfMeasureIds.ProtectedLengthUnitIds)
        {
            PackagingMaterialRules.IsValidStandardUnit(id).Should().BeTrue();
        }
    }

    [Fact]
    public void IsValidStandardUnit_is_false_for_gram()
    {
        PackagingMaterialRules.IsValidStandardUnit(WellKnownUnitOfMeasureIds.Gram).Should().BeFalse();
    }

    [Fact]
    public void EnsureValidStandardUnit_throws_for_gram()
    {
        var act = () => PackagingMaterialRules.EnsureValidStandardUnit(WellKnownUnitOfMeasureIds.Gram);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyLatestPrice_sets_CurrentCost_from_the_latest_entry()
    {
        var packagingMaterial = new PackagingMaterial();
        var latest = new PackagingPriceHistory { Price = 45m };

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latest);

        packagingMaterial.CurrentCost.Should().Be(45m);
    }

    [Fact]
    public void ApplyLatestPrice_with_no_entries_zeroes_CurrentCost()
    {
        var packagingMaterial = new PackagingMaterial { CurrentCost = 45m };

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, null);

        packagingMaterial.CurrentCost.Should().Be(0m);
    }
}
