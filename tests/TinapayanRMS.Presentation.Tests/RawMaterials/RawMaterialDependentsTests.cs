using AwesomeAssertions;
using TinapayanRMS.Presentation.RawMaterials;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>How the Dependents tiles word a material's share of a product's material cost (MaterialCostShares works it out).</summary>
public class RawMaterialDependentsTests
{
    [Test]
    public void A_share_reads_as_a_percentage_of_materials()
    {
        RawMaterialDependents.ShareText(0.38m).Should().Be("38% of materials");
        RawMaterialDependents.ShareText(0.90m / 11.08m).Should().Be("8.1% of materials");
        RawMaterialDependents.ShareText(0.0004m).Should().Be("Under 0.1% of materials");
        RawMaterialDependents.ShareText(null).Should().BeNull();
    }
}
