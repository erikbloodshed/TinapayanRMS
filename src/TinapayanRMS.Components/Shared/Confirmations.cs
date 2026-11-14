using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A question that needs an answer before something that can't be undone, answered by <see cref="ConfirmationHost"/>'s
/// dialog: a ViewModel's ConfirmDelete, Yes or No; or its
/// ConfirmUnsavedChanges, Yes, No or Cancel, before a form with unsaved changes is left. One per page the window loads. Asking again
/// while a question is open answers the open one No, or Cancel.
/// </summary>
public sealed class Confirmations
{
    private TaskCompletionSource<UnsavedChangesAnswer>? _pending;

    /// <summary>The question being asked, or null when none is.</summary>
    public string? Question { get; private set; }

    /// <summary>Whether it's about unsaved changes, with Cancel beside Yes and No.</summary>
    public bool HasCancel { get; private set; }

    /// <summary>Raised when a question opens or is answered.</summary>
    public event Action? Changed;

    public async Task<bool> AskAsync(string question) => await OpenAsync(question, hasCancel: false) == UnsavedChangesAnswer.Save;

    /// <summary>Save the changes (Yes), throw them away (No), or stay (Cancel, or the dialog closed any other way).</summary>
    public Task<UnsavedChangesAnswer> AskAboutUnsavedChangesAsync(string question) => OpenAsync(question, hasCancel: true);

    private Task<UnsavedChangesAnswer> OpenAsync(string question, bool hasCancel)
    {
        _pending?.TrySetResult(HasCancel ? UnsavedChangesAnswer.Cancel : UnsavedChangesAnswer.DontSave);
        _pending = new TaskCompletionSource<UnsavedChangesAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        Question = question;
        HasCancel = hasCancel;
        Changed?.Invoke();
        return _pending.Task;
    }

    /// <summary>Yes or No.</summary>
    public void Answer(bool yes) => Answer(yes ? UnsavedChangesAnswer.Save : UnsavedChangesAnswer.DontSave);

    /// <summary>The dialog closed without Yes or No: No for a plain question, Cancel for one about unsaved changes.</summary>
    public void Dismiss() => Answer(HasCancel ? UnsavedChangesAnswer.Cancel : UnsavedChangesAnswer.DontSave);

    public void Answer(UnsavedChangesAnswer answer)
    {
        var pending = _pending;
        _pending = null;
        Question = null;
        HasCancel = false;
        Changed?.Invoke();
        pending?.TrySetResult(answer);
    }
}
