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
}
