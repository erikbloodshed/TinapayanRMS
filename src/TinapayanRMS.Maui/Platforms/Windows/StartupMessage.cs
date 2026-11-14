using System.Runtime.InteropServices;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// Windows' own message box, for what has to be said before the app has a window to say it in: a start that failed
/// (MauiProgram), which otherwise just disappeared, and a start refused because the app is open in another Windows
/// sign-in (SingleInstance). Shown in front, and waited for.
/// </summary>
public static partial class StartupMessage
{
    private const uint Ok = 0x0;
    private const uint IconError = 0x10;
    private const uint IconInformation = 0x40;
    private const uint SetForeground = 0x10000;

    public static void ShowError(string text) => _ = MessageBox(0, text, "TinapayanRMS", Ok | IconError | SetForeground);

    public static void ShowInformation(string text) => _ = MessageBox(0, text, "TinapayanRMS", Ok | IconInformation | SetForeground);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
