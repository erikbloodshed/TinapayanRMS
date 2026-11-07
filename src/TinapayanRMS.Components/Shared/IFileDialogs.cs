namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The computer's own file dialogs, in place of the web app's downloads and uploads: the WPF app's Save, Open and folder
/// dialogs, which the ViewModels' interactions were written for. Each answers with a path, or null when cancelled. The
/// MAUI app gives each platform's (Platforms/Windows, Platforms/MacCatalyst).
/// </summary>
public interface IFileDialogs
{
    /// <summary>Where to save a file, suggesting <paramref name="fileName"/>; a file of the same kind, by its extension.</summary>
    /// <param name="kind">What the file is, for the dialog's list of kinds ("Excel workbook").</param>
    Task<string?> ChooseSaveFileAsync(string fileName, string kind);

    /// <summary>A file to open, of the kind <paramref name="extension"/> names (".backup").</summary>
    Task<string?> ChooseFileToOpenAsync(string extension, string kind);

    Task<string?> ChooseFolderAsync();
}

public static class FileDialogsExtensions
{
    /// <summary>Asks where to save <paramref name="content"/> and saves it there: the path, or null when cancelled.</summary>
    public static async Task<string?> SaveAsync(this IFileDialogs dialogs, string fileName, string kind, ReadOnlyMemory<byte> content)
    {
        var path = await dialogs.ChooseSaveFileAsync(fileName, kind);
        if (path is not null)
        {
            await File.WriteAllBytesAsync(path, content);
        }

        return path;
    }
}
