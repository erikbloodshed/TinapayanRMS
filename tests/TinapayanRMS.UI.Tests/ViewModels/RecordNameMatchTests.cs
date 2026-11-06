using AwesomeAssertions;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>RecordNameMatch: the record the Vim command line's <c>:e name</c> means.</summary>
public class RecordNameMatchTests
{
    private static readonly string[] Names = ["Ube Cheese Pandesal", "Pandesal", "Cheese Bread", "Spanish Bread", "pandan Cake"];

    [Test]
    [Arguments("pandesal", "Pandesal")]
    [Arguments("  PANDESAL ", "Pandesal")]
    [Arguments("pan", "pandan Cake")]
    [Arguments("bread", "Cheese Bread")]
    [Arguments("cheese", "Cheese Bread")]
    [Arguments("ube", "Ube Cheese Pandesal")]
    public void The_exact_name_wins_then_a_name_starting_with_it_then_one_holding_it_then_the_first_by_name(string typed, string expected)
    {
        RecordNameMatch.Best(Names, name => name, typed).Should().Be(expected);
    }

    [Test]
    [Arguments("croissant")]
    [Arguments("")]
    [Arguments("   ")]
    public void Nothing_matching_or_nothing_typed_is_no_record(string typed)
    {
        RecordNameMatch.Best(Names, name => name, typed).Should().BeNull();
    }
}
