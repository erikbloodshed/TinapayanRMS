namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A change made in one go recorded as one undo step, by a screen or by a part of one through its screen.</summary>
public static class UndoRecorderExtensions
{
    /// <summary>
    /// Records <paramref name="change"/> as one undo step of <paramref name="recorder"/>'s form: a command that makes its
    /// change in one go, whether the screen's own or a part's (the Packaging tab's Remove Line, say).
    /// </summary>
    public static void AsUndoStep(this IUndoRecorder recorder, Action change)
    {
        recorder.BeginUndoStep();
        try
        {
            change();
        }
        finally
        {
            recorder.EndUndoStep();
        }
    }
}
