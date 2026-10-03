using AwesomeAssertions;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>HintLabels: as short as the count allows, and none the start of another, so a whole label always picks one.</summary>
public class HintLabelsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(500)]
    public void Labels_are_as_many_as_asked_unique_and_none_the_start_of_another(int count)
    {
        var labels = HintLabels.For(count);

        labels.Should().HaveCount(count).And.OnlyHaveUniqueItems();
        labels.Should().AllSatisfy(label => label.Should().MatchRegex("^[" + HintLabels.Alphabet + "]+$"));
        foreach (var label in labels)
        {
            labels.Where(other => !ReferenceEquals(other, label)).Should().NotContain(other => other.StartsWith(label, StringComparison.Ordinal), $"typing {label} must pick it alone");
        }
    }

    [Fact]
    public void A_screenful_that_fits_the_alphabet_gets_one_letter_each()
    {
        HintLabels.For(HintLabels.Alphabet.Length).Should().AllSatisfy(label => label.Should().HaveLength(1));
    }

    [Fact]
    public void One_more_than_the_alphabet_needs_two_letters_for_only_some()
    {
        var labels = HintLabels.For(HintLabels.Alphabet.Length + 1);

        labels.Max(label => label.Length).Should().Be(2);
        labels.Count(label => label.Length == 1).Should().Be(HintLabels.Alphabet.Length - 1);
    }

    [Fact]
    public void None_asked_for_is_none()
    {
        HintLabels.For(0).Should().BeEmpty();
    }
}
