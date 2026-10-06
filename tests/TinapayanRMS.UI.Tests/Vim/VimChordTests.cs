using System.Windows.Input;
using AwesomeAssertions;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>The Ctrl chords Vim gives other keys: Ctrl+[ is Esc, and Ctrl+N and Ctrl+P are Down and Up in an open list.</summary>
public class VimChordTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_and_open_bracket_is_Esc_list_or_not(bool isListOpen)
    {
        VimKeys.KeyForChord(Key.OemOpenBrackets, ModifierKeys.Control, isListOpen).Should().Be(Key.Escape);
    }

    [Fact]
    public void Ctrl_N_and_Ctrl_P_are_Down_and_Up_in_an_open_list()
    {
        VimKeys.KeyForChord(Key.N, ModifierKeys.Control, isListOpen: true).Should().Be(Key.Down);
        VimKeys.KeyForChord(Key.P, ModifierKeys.Control, isListOpen: true).Should().Be(Key.Up);
    }

    [Theory]
    [InlineData(Key.N)]
    [InlineData(Key.P)]
    public void Ctrl_N_and_Ctrl_P_are_left_alone_with_no_list_open(Key key)
    {
        VimKeys.KeyForChord(key, ModifierKeys.Control, isListOpen: false).Should().BeNull();
    }

    [Theory]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.None)]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.P, ModifierKeys.Alt)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.Control)]
    [InlineData(Key.R, ModifierKeys.Control)]
    public void Anything_else_is_not_a_chord(Key key, ModifierKeys modifiers)
    {
        VimKeys.KeyForChord(key, modifiers, isListOpen: true).Should().BeNull();
    }
}
