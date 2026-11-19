using Microsoft.AspNetCore.Components;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A record view's form, as its tab shows it (SubAssemblyFormView): a screen over the form it's given rather than the
/// page's own state, drawn afresh each time its tab shows (keyed by the form). It shows the form's messages, answers its
/// questions, redraws as it changes and puts it on show for the Vim keys and the undo keys, as any screen does; but it
/// loads nothing (the view's load, run as the view's page opens, merges into every form), and guards no unsaved changes on
/// a move to another address: the form stays in its tab, and the window asks about it as it closes (IOpenForms).
/// </summary>
public abstract class FormBase<TForm> : ScreenBase<TForm>
    where TForm : ScreenState
{
    /// <summary>The form the tab holds.</summary>
    [Parameter, EditorRequired] public TForm Form { get; set; } = null!;

    protected override TForm ResolveState() => Form;

    protected override ScreenCommand? LoadCommand => null;

    protected override IUnsavedChangesScreen? UnsavedChangesForm => null;
}
