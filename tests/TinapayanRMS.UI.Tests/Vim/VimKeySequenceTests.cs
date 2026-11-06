using AwesomeAssertions;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>VimKeySequence: typed characters into Vim actions, holding the first of a two-key sequence.</summary>
public class VimKeySequenceTests
{
    [Test]
    [Arguments('j', VimAction.Down)]
    [Arguments('k', VimAction.Up)]
    [Arguments('h', VimAction.Left)]
    [Arguments('l', VimAction.Right)]
    [Arguments('G', VimAction.Last)]
    [Arguments('$', VimAction.LineEnd)]
    [Arguments('0', VimAction.LineStart)]
    [Arguments('f', VimAction.Hints)]
    [Arguments(':', VimAction.CommandLine)]
    [Arguments('?', VimAction.CheatSheet)]
    [Arguments('/', VimAction.Search)]
    [Arguments('u', VimAction.Undo)]
    public void A_single_key_is_its_action_at_once(char key, VimAction action)
    {
        var sequence = new VimKeySequence();

        sequence.Feed(key).Should().Be(action);
        sequence.Pending.Should().Be('\0');
    }

    [Test]
    [Arguments("gg", VimAction.First)]
    [Arguments("gt", VimAction.NextTab)]
    [Arguments("gT", VimAction.PreviousTab)]
    [Arguments("dd", VimAction.Delete)]
    [Arguments("cc", VimAction.Change)]
    public void A_two_key_sequence_waits_for_its_second_key(string keys, VimAction action)
    {
        var sequence = new VimKeySequence();

        sequence.Feed(keys[0]).Should().Be(VimAction.None);
        sequence.Pending.Should().Be(keys[0]);
        sequence.Feed(keys[1]).Should().Be(action);
        sequence.Pending.Should().Be('\0');
    }

    [Test]
    public void A_second_key_that_makes_no_sequence_drops_both_as_Vim_does()
    {
        var sequence = new VimKeySequence();
        sequence.Feed('g');

        sequence.Feed('j').Should().Be(VimAction.None, "gj isn't one of ours, and the j goes with the g");
        sequence.Pending.Should().Be('\0');
        sequence.Feed('j').Should().Be(VimAction.Down, "the next key starts afresh");
    }

    [Test]
    public void Clear_drops_a_half_typed_sequence_and_says_whether_there_was_one()
    {
        var sequence = new VimKeySequence();
        sequence.Feed('d');

        sequence.Clear().Should().BeTrue();
        sequence.Pending.Should().Be('\0');
        sequence.Clear().Should().BeFalse();
        sequence.Feed('d').Should().Be(VimAction.None, "a d after the clear starts a new dd");
    }

    [Test]
    public void A_key_that_means_nothing_is_none_and_leaves_nothing_pending()
    {
        var sequence = new VimKeySequence();

        sequence.Feed('x').Should().Be(VimAction.None);
        sequence.Pending.Should().Be('\0');
    }
}
