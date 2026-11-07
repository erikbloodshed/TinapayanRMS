using Windows.Storage.Pickers;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// Windows' own Save, Open and folder dialogs, over the app's window. An unpackaged app has to give each its window's
/// handle before showing it.
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

    private static void OverWindow(object picker)
    {
        var window = (Application.Current?.Windows is [var first, ..] ? first.Handler?.PlatformView : null) as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("The app has no window to show the dialog over.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
    }
}
