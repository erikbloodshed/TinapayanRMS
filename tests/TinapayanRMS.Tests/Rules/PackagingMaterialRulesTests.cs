using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Rules;

public class PackagingMaterialRulesTests
{
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
