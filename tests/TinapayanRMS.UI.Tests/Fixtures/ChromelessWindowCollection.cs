using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// Every test that builds a real Syncfusion ChromelessWindow (the app's AppWindow is one). They run one at a time:
/// ChromelessWindow's native chrome keeps its message windows in one static Dictionary for the whole process
/// (Standard.MessageWindow in Syncfusion.Shared.WPF), with no lock, and windows made on separate STA threads at once
/// corrupted it, which ended the test process (IndexOutOfRangeException in Dictionary.TryInsert, 0xC000041D). The app has
/// one UI thread, so it only ever made them one at a time. It first showed once the ViewModel tests moved to
/// Presentation.Tests (2026-11-07), when these classes came to start together instead of among those.
/// Such a class carries <c>[NotInParallel(ChromelessWindowCollection.Name)]</c>.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Named for the collection it names.")]
public static class ChromelessWindowCollection
{
    public const string Name = "Chromeless window";
}
