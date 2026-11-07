using System.Diagnostics;
using Windows.Storage.Pickers;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// Windows' own Save, Open and folder dialogs, over the app's window, and File Explorer. An unpackaged app has to give
/// each dialog its window's handle before showing it.
/// </summary>
public sealed class FileDialogs : IFileDialogs
{
    public async Task<string?> ChooseSaveFileAsync(string fileName, string kind)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(fileName),
        };
        picker.FileTypeChoices.Add(kind, [Path.GetExtension(fileName)]);
        OverWindow(picker);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> ChooseFileToOpenAsync(string extension, string kind)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        OverWindow(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> ChooseFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        OverWindow(picker);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>As the WPF app's Open Folder did: Explorer, which takes a quoted path as the folder to open.</summary>
    public void ShowFolder(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();

    private static void OverWindow(object picker)
    {
        var window = (Application.Current?.Windows is [var first, ..] ? first.Handler?.PlatformView : null) as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("The app has no window to show the dialog over.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
    }
}
