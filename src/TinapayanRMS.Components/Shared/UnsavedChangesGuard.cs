using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The form on show, for the window to ask about before it closes, as the WPF window did: closing a window isn't a
/// move to another address, so ScreenBase's own question never
/// sees it. ScreenBase keeps the form here while its screen is up. One per page the window loads.
/// </summary>
public sealed class UnsavedChangesGuard
{
    private IUnsavedChangesScreen? _form;

    /// <summary>Whether the form on show holds anything a Save would write.</summary>
    public bool HasUnsavedChanges => _form?.HasUnsavedChanges == true;

    /// <summary>Keeps <paramref name="form"/> as the one on show until the returned subscription goes.</summary>
    public IDisposable Watch(IUnsavedChangesScreen form)
    {
        _form = form;
        return new Release(this, form);
    }

    /// <summary>
    /// Asks about the form's unsaved changes, Save, Don't Save or Cancel, as leaving it does. True when the window may
    /// close: nothing was unsaved, the Save went through, or the user chose not to save.
    /// </summary>
    public Task<bool> ResolveBeforeClosingAsync() =>
        _form is { HasUnsavedChanges: true } form ? form.ResolveUnsavedChangesAsync(revert: false) : Task.FromResult(true);

    private sealed class Release(UnsavedChangesGuard guard, IUnsavedChangesScreen form) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(guard._form, form))
            {
                guard._form = null;
            }
        }
    }
}
