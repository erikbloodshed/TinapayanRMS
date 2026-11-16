using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// What the window asks before it closes. Closing a window isn't a move to another address, so
/// ScreenBase's own question never sees it. Two things keep it open: work that closing would cut short (<see cref="IBusyWork"/>:
/// a backup or restore running in Settings → Database), which the window waits for, saying why; and a form's unsaved
/// changes, asked about as leaving the form asks. ScreenBase keeps its form here while its screen is up. A form whose
/// screen went while it still held unsaved changes is kept too: a screen that failed gives way to its error panel
/// (ScreenErrorBoundary) without the question leaving it would have asked, and what it held is still its ViewModel's. One
/// per page the window loads.
/// </summary>
/// <param name="services">The page's services, for the screens that may be busy: asked only as the window closes.</param>
/// <param name="notices">The page's notices, to say why the window stays open.</param>
public sealed class UnsavedChangesGuard(IServiceProvider services, Notices notices)
{
    /// <summary>The forms on show, by how many screens show each (Products and Print show the same one).</summary>
    private readonly Dictionary<IUnsavedChangesScreen, int> _shown = new(ReferenceEqualityComparer.Instance);

    /// <summary>Forms whose screens went while they held unsaved changes.</summary>
    private readonly HashSet<IUnsavedChangesScreen> _leftUnsaved = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether a form on show, or one left with them, holds anything a Save would write.</summary>
    public bool HasUnsavedChanges => Forms().Any(form => form.HasUnsavedChanges);

    /// <summary>Keeps <paramref name="form"/> as one on show until the returned subscription goes.</summary>
    public IDisposable Watch(IUnsavedChangesScreen form)
    {
        _shown[form] = _shown.GetValueOrDefault(form) + 1;
        _leftUnsaved.Remove(form);
        return new Release(this, form);
    }

    /// <summary>
    /// True when the window may close: nothing is running that closing would cut short, and every form with unsaved
    /// changes had them saved, or the user chose not to save. While something is running, the notices say what, and the
    /// window stays open; Cancel on a form keeps it open too.
    /// </summary>
    public async Task<bool> ResolveBeforeClosingAsync()
    {
        foreach (var work in services.GetServices<IBusyWork>())
        {
            if (work.KeepsWindowOpen is { } why)
            {
                notices.ShowError(why);
                return false;
            }
        }

        foreach (var form in Forms().Where(form => form.HasUnsavedChanges).ToList())
        {
            if (!await form.ResolveUnsavedChangesAsync(revert: false))
            {
                return false;
            }
        }

        _leftUnsaved.RemoveWhere(form => !form.HasUnsavedChanges);
        return true;
    }

    private IEnumerable<IUnsavedChangesScreen> Forms() => _shown.Keys.Concat(_leftUnsaved);

    private sealed class Release(UnsavedChangesGuard guard, IUnsavedChangesScreen form) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            if (guard._shown.GetValueOrDefault(form) <= 1)
            {
                guard._shown.Remove(form);
                if (form.HasUnsavedChanges)
                {
                    guard._leftUnsaved.Add(form);
                }
            }
            else
            {
                guard._shown[form]--;
            }
        }
    }
}
