using ReactiveUI;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI;

/// <summary>
/// Decides whether the app carries on after an exception nothing else caught on the UI thread, rather than closing and
/// losing whatever hasn't been saved. The screens' own failures, a database that can't be reached among them, show in
/// their error notices (ViewModelBase), so what still gets here is a bug, or a failure in a control's own callback, and saying so is better than closing. But the same failure can
/// come round again at once (one thrown on every layout pass), and a message after a message would leave the app unusable
/// anyway: more than <see cref="MaxFailures"/> within <see cref="Window"/> and it's let close, as it used to.
/// </summary>
public sealed class UnhandledErrorGuard
{
    public const int MaxFailures = 3;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTime> _recent = new();

    /// <summary>Records a failure at <paramref name="now"/>, and returns whether the app should carry on after it.</summary>
    public bool Admit(DateTime now)
    {
        while (_recent.TryPeek(out var oldest) && now - oldest > Window)
        {
            _recent.Dequeue();
        }

        _recent.Enqueue(now);
        return _recent.Count <= MaxFailures;
    }

    /// <summary>
    /// What's worth showing of an exception: a failure ReactiveUI was left holding (a command's or an observable's) arrives
    /// wrapped in its UnhandledErrorException, and it's the cause inside.
    /// </summary>
    public static Exception CauseOf(Exception exception) =>
        exception is UnhandledErrorException { InnerException: { } inner } ? inner : exception;

    /// <summary>What the app says about <paramref name="cause"/> as it carries on.</summary>
    public static string CarryOnText(Exception cause) =>
        Failure.Of(cause).Text
        + Environment.NewLine + Environment.NewLine
        + "The app is still running, and nothing you haven't saved was lost. "
        + "If something looks wrong, save what you can, then close the app and open it again.";
}
