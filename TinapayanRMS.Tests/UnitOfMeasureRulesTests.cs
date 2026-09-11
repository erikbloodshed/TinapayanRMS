using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class UnitOfMeasureRulesTests
{
    [Fact]
    public void IsProtected_is_true_for_every_length_unit()
    {
        foreach (var id in WellKnownUnitOfMeasureIds.ProtectedLengthUnitIds)
        {
            UnitOfMeasureRules.IsProtected(id).Should().BeTrue();
        }
    }

    [Fact]
    public void IsProtected_is_false_for_gram_and_piece()
    {
        UnitOfMeasureRules.IsProtected(5).Should().BeFalse();
        UnitOfMeasureRules.IsProtected(6).Should().BeFalse();
    }

    [Fact]
    public void IsProtected_is_false_for_an_id_that_does_not_exist()
    {
        UnitOfMeasureRules.IsProtected(999).Should().BeFalse();
    }
}
