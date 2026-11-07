using Serilog;

namespace TinapayanRMS.Components;

/// <summary>
/// The MAUI app's view state starts as a copy of the WPF app's, the first time it runs for the Windows user, so it opens
/// with the lists' folders as they were left, the Vim keys as they were set, and the backups' second folder. Never again
/// after that: from then on each app keeps its own file, since each reads its file once and writes it whole, and two
/// sharing one would write over each other's changes.
/// </summary>
public static class ViewStateSeed
{
    /// <summary>Copies <paramref name="wpfFile"/> to <paramref name="file"/>, unless there's one there already or none to copy.</summary>
    public static void StartFrom(string wpfFile, string file)
    {
        if (File.Exists(file) || !File.Exists(wpfFile))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(wpfFile, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It only holds conveniences: the app starts without them.
            Log.Warning(ex, "Couldn't start the view state from the WPF app's {File}", wpfFile);
        }
    }
}
