using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Shared;

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

    [Test]
    public void A_name_with_a_word_starting_with_the_text_comes_before_one_only_holding_it()
    {
        RecordNameMatch.Best(["Unsalted Butter", "Sea Salt"], name => name, "salt").Should().Be("Sea Salt");
    }

    [Test]
    [Arguments("Salt", 0)]
    [Arguments("Salted Egg", 1)]
    [Arguments("Sea Salt", 2)]
    [Arguments("Unsalted Salt", 2)]
    [Arguments("Unsalted Butter", 3)]
    public void A_pickers_list_is_sorted_by_how_well_each_name_matches(string name, int rank)
    {
        RecordNameMatch.Rank(name, " SALT ").Should().Be(rank);
    }

    [Test]
    [Arguments("Flour", "salt")]
    [Arguments("Salt", "")]
    [Arguments("Salt", "   ")]
    public void A_name_not_holding_the_text_or_nothing_typed_has_no_rank(string name, string typed)
    {
        RecordNameMatch.Rank(name, typed).Should().BeNull();
    }
}
