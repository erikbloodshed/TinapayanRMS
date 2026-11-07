using System.Diagnostics;
using System.IO;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The Settings dialog's Database tab: the backups, and Back Up Now, Copy to…, Restore and Restore from File…. Built
/// afresh with each Settings, over the long-lived ViewModel, so its dialog handlers go with it (Dispose, from SettingsWindow).
/// </summary>
public sealed partial class DatabaseBackupsView : UserControl, IDisposable
{
    private readonly CompositeDisposable _registrations;

    public DatabaseBackupsView(DatabaseBackupsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _registrations = new CompositeDisposable(
            viewModel.RegisterNativeMessageBoxHandler(),

            // Copy to…: where to put the picked backup, under its own name by default.
            viewModel.ChooseCopyDestination.RegisterHandler(interaction =>
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Copy Backup To",
                    Filter = $"Database backup (*{BackupRules.Extension})|*{BackupRules.Extension}",
                    DefaultExt = BackupRules.Extension,
                    FileName = interaction.Input,
                    OverwritePrompt = true,
                };
                interaction.SetOutput(dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null);
            }),

            // Restore from File…: a backup from a USB stick or another computer.
            viewModel.ChooseCopyFolder.RegisterHandler(interaction =>
            {
                var dialog = new OpenFolderDialog { Title = "Copy Automatic Backups To" };
                interaction.SetOutput(dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null);
            }),

            viewModel.ChooseBackupFile.RegisterHandler(interaction =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Restore from File",
                    Filter = $"Database backup (*{BackupRules.Extension})|*{BackupRules.Extension}|All files (*.*)|*.*",
                    CheckFileExists = true,
                };
                interaction.SetOutput(dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null);
            }));
    }

    /// <summary>Shows the backups folder in File Explorer, once a backup has made it.</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = ((DatabaseBackupsViewModel)DataContext).Folder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }

    public void Dispose()
    {
        _registrations.Dispose();
        GC.SuppressFinalize(this);
    }
}
