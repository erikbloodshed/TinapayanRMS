using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Settings' Keyboard tab: whether the Vim keys are on. Kept for the Windows user on this computer, with the
/// lists' open folders (IViewStateStore), not in the database: it's how one person likes to work, not the bakeshop's data.
/// </summary>
public sealed class KeyboardSettingsViewModel : ObservableState
{
    /// <summary>The view-state key the setting is kept under: <c>["on"]</c> when the keys are on.</summary>
    public const string VimKeysKey = "vim-keys";

    private readonly IViewStateStore _viewState;
    private readonly Action<bool> _applyVimKeys;
    private bool _isVimKeysOn;

    /// <param name="viewState">Where the setting is kept.</param>
    /// <param name="applyVimKeys">Turns the keys on or off in the running app as the box is ticked or cleared.</param>
    public KeyboardSettingsViewModel(IViewStateStore viewState, Action<bool> applyVimKeys)
    {
        _viewState = viewState;
        _applyVimKeys = applyVimKeys;
        _isVimKeysOn = IsVimKeysSaved(viewState);
    }

    /// <summary>Whether the Vim keys were left on, which App reads as it starts.</summary>
    public static bool IsVimKeysSaved(IViewStateStore viewState) => viewState.ReadList(VimKeysKey) is ["on"];

    public bool IsVimKeysOn
    {
        get => _isVimKeysOn;
        set
        {
            if (RaiseAndSetIfChanged(ref _isVimKeysOn, value))
            {
                _viewState.WriteList(VimKeysKey, value ? ["on"] : []);
                _applyVimKeys(value);
            }
        }
    }
}
