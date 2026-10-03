namespace TinapayanRMS.UI;

/// <summary>
/// Decides whether the app carries on after an exception nothing else caught on the UI thread (2026-10-03). Until then
/// App only logged one and the app closed, losing whatever hadn't been saved; the screens' own failures, a database that
/// can't be reached among them, have shown in their error notices since the same day (ViewModelBase), so what still gets
/// here is a bug, or a failure in a control's own callback, and saying so is better than closing. But the same failure can
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
}
