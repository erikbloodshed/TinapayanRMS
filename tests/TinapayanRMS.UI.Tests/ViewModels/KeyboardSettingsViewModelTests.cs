using AwesomeAssertions;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>KeyboardSettingsViewModel: the Vim keys' box on Settings' Keyboard tab, kept for the Windows user.</summary>
public class KeyboardSettingsViewModelTests
{
    [Test]
    public void The_keys_start_off_for_a_user_who_never_turned_them_on()
    {
        var store = new InMemoryViewStateStore();

        new KeyboardSettingsViewModel(store, _ => { }).IsVimKeysOn.Should().BeFalse();
        KeyboardSettingsViewModel.IsVimKeysSaved(store).Should().BeFalse();
    }

    [Test]
    public void Ticking_the_box_keeps_the_choice_and_turns_the_keys_on_in_the_running_app()
    {
        var store = new InMemoryViewStateStore();
        var applied = new List<bool>();
        var viewModel = new KeyboardSettingsViewModel(store, applied.Add);

        viewModel.IsVimKeysOn = true;

        applied.Should().Equal(true);
        KeyboardSettingsViewModel.IsVimKeysSaved(store).Should().BeTrue();
        new KeyboardSettingsViewModel(store, _ => { }).IsVimKeysOn.Should().BeTrue("the next run starts with them on");

        viewModel.IsVimKeysOn = false;

        applied.Should().Equal(true, false);
        KeyboardSettingsViewModel.IsVimKeysSaved(store).Should().BeFalse();
    }

    [Test]
    public void Setting_the_box_to_what_it_already_is_writes_nothing()
    {
        var store = new InMemoryViewStateStore(new Dictionary<string, IReadOnlyList<string>> { [KeyboardSettingsViewModel.VimKeysKey] = ["on"] });
        var applied = new List<bool>();
        var viewModel = new KeyboardSettingsViewModel(store, applied.Add);

        viewModel.IsVimKeysOn = true;

        store.WriteCount.Should().Be(0);
        applied.Should().BeEmpty();
    }
}
