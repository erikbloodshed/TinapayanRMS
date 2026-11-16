using AwesomeAssertions;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>KeyboardSettingsState: the Vim keys' box on Settings' Keyboard tab, kept for the Windows user.</summary>
public class KeyboardSettingsStateTests
{
    [Test]
    public void The_keys_start_off_for_a_user_who_never_turned_them_on()
    {
        var store = new InMemoryViewStateStore();

        new KeyboardSettingsState(store, _ => { }).IsVimKeysOn.Should().BeFalse();
        KeyboardSettingsState.IsVimKeysSaved(store).Should().BeFalse();
    }

    [Test]
    public void Ticking_the_box_keeps_the_choice_and_turns_the_keys_on_in_the_running_app()
    {
        var store = new InMemoryViewStateStore();
        var applied = new List<bool>();
        var state = new KeyboardSettingsState(store, applied.Add);

        state.IsVimKeysOn = true;

        applied.Should().Equal(true);
        KeyboardSettingsState.IsVimKeysSaved(store).Should().BeTrue();
        new KeyboardSettingsState(store, _ => { }).IsVimKeysOn.Should().BeTrue("the next run starts with them on");

        state.IsVimKeysOn = false;

        applied.Should().Equal(true, false);
        KeyboardSettingsState.IsVimKeysSaved(store).Should().BeFalse();
    }

    [Test]
    public void Setting_the_box_to_what_it_already_is_writes_nothing()
    {
        var store = new InMemoryViewStateStore(new Dictionary<string, IReadOnlyList<string>> { [KeyboardSettingsState.VimKeysKey] = ["on"] });
        var applied = new List<bool>();
        var state = new KeyboardSettingsState(store, applied.Add);

        state.IsVimKeysOn = true;

        store.WriteCount.Should().Be(0);
        applied.Should().BeEmpty();
    }
}
