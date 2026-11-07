namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The open screen's ErrorMessage and SuccessMessage, for <see cref="NoticeHost"/> to show as toasts in the page's
/// bottom-right corner, as the WPF app's ErrorToast and SuccessToast did. One per page the window loads; the screen that's open passes
/// on its ViewModel's messages as they change (<see cref="ScreenBase{TViewModel}"/>). A null message hides its toast:
/// RunSafelyAsync clears both before every action, so the same error twice still shows twice.
/// </summary>
public sealed class Notices
{
    /// <summary>Raised with the toast to show or hide, on whichever thread the ViewModel changed on.</summary>
    public event Action<NoticeKind, string?>? Changed;

    public void ShowError(string? message) => Changed?.Invoke(NoticeKind.Error, message);

    public void ShowSuccess(string? message) => Changed?.Invoke(NoticeKind.Success, message);
}

public enum NoticeKind
{
    /// <summary>Stays until its close button is clicked or the error clears.</summary>
    Error,

    /// <summary>Fades a few seconds after it opens, but not while the mouse is over it.</summary>
    Success,
}
