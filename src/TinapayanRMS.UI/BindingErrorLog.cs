#if DEBUG
using System.Diagnostics;
using System.Globalization;
using Serilog;

namespace TinapayanRMS.UI;

/// <summary>
/// Writes WPF's data-binding errors to the log. WPF reports a failed binding only while a debugger is attached, and
/// the app mostly runs without one, so a binding to a renamed or missing property went unseen. App adds this in Debug
/// builds only.
/// </summary>
internal sealed class BindingErrorLog : TraceListener
{
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        Log.Warning("WPF binding {EventType} {Id}: {Message}", eventType, id, message);

    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        TraceEvent(eventCache, source, eventType, id, format is null || args is null ? format : string.Format(CultureInfo.InvariantCulture, format, args));

    // WPF traces through TraceEvent. These take anything written to the listener directly.
    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message) => Log.Warning("WPF binding: {Message}", message);
}
#endif
