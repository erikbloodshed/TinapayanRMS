using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;
using Syncfusion.Themes.Windows11Light.WPF;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Controls.Input;
using Syncfusion.Windows.Shared;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;
using TinapayanRMS.UI.Vim;
using TinapayanRMS.UI.Views;

namespace TinapayanRMS.UI;

public sealed partial class App : Application
{
    /// <summary>The height of every text box and button, in device-independent pixels.</summary>
    private const double ControlHeight = 24;

    /// <summary>Where the log is written, beside the app.</summary>
    private static readonly string LogFolder = Path.Combine(AppContext.BaseDirectory, "logs");

    private IHost? _host;

    private readonly UnhandledErrorGuard _errorGuard = new();

    // Keeps the app to this copy, from before the window shows until the log is closed.
    private readonly SingleInstanceGuard _singleInstance = new(SingleInstanceGuard.AppName);

    // Set while an unhandled exception's message is up, so another arriving meanwhile is only logged.
    private bool _isShowingError;

    // Set by RestartAfterExit: the app starts again once this one has closed.
    private bool _restartOnExit;

    /// <summary>
    /// Closing to start again after a restore (RestartAfterExit). The main window doesn't ask about unsaved changes then:
    /// the restore replaced the data they were made to, and its question already said they'd be lost.
    /// </summary>
    public bool IsRestarting => _restartOnExit;

    static App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(LogFolder, "tinapayanrms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

#if DEBUG
        // Failed bindings go to the log (BindingErrorLog). Refresh is what turns WPF's tracing on without a debugger.
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorLog());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
#endif

        try
        {
            // The build compiles the key in (see the UI project), so an installed app needs no environment variable.
            var licenseKey = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "SyncfusionLicenseKey")?.Value
                ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
                ?? throw new InvalidOperationException(
                    "No Syncfusion licence key: it wasn't built into the app, and SYNCFUSION_LICENSE_KEY isn't set.");

            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed before the main window could be created");
            throw;
        }

        // The Windows11Light theme's SfTextBoxExt style — unlike its ComboBoxAdv, DoubleTextBox and
        // DateTimeEdit styles — never sets VerticalContentAlignment, so text falls back to
        // Control's default of Top and sits against the top of any box taller than its text
        // (every one, at SizeMode="Touch"'s 32px). Changing the type's default here fixes every instance,
        // including future ones, without a Style on the control. Must run before any
        // SfTextBoxExt exists, which is why it lives in this static constructor.
        Control.VerticalContentAlignmentProperty.OverrideMetadata(
            typeof(SfTextBoxExt), new FrameworkPropertyMetadata(VerticalAlignment.Center));

        // Every text box and button is 24px tall. At SizeMode="Default" the theme sets no height on
        // them, so each took its content's: an SfTextBoxExt 18-20px, a DoubleTextBox 22px, a
        // ButtonAdv 24px, or whatever its layout row stretched it to. A type default is the lowest
        // precedence, so a Height set in XAML still wins. IntegerTextBox and CurrencyTextBox (Products'
        // Quantity Required and Current Price) joined on 2026-09-24: with their labels moved beside them
        // they stood shorter than the Product Name box next to them. PercentTextBox (the Costing tab's metrics) joined
        // on 2026-09-28, beside the DoubleTextBox that holds each metric's value.
        foreach (var type in new[] { typeof(SfTextBoxExt), typeof(DoubleTextBox), typeof(IntegerTextBox), typeof(CurrencyTextBox), typeof(PercentTextBox), typeof(ButtonAdv) })
        {
            FrameworkElement.HeightProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(ControlHeight));
        }

        EventManager.RegisterClassHandler(
            typeof(SfTextBoxExt), FrameworkElement.LoadedEvent, new RoutedEventHandler(AlignTextBoxText));

        EventManager.RegisterClassHandler(
            typeof(EditorBase), FrameworkElement.LoadedEvent, new RoutedEventHandler(AlignNumberRight));

        EventManager.RegisterClassHandler(
            typeof(ComboBoxAdv), FrameworkElement.LoadedEvent, new RoutedEventHandler(ApplyPickerDefaults));

        EventManager.RegisterClassHandler(
            typeof(AddNewRowControl), FrameworkElement.LoadedEvent, new RoutedEventHandler(HideAddNewRowText));

        // Keyboard focus in a grid sits on a cell: no dotted rectangle round the whole grid, and Tab lands on a row.
        GridKeyboardFocus.Register();

        // Every grid's header row tall enough for its text at the app's font size.
        GridHeaderHeight.Register();

        // Every picker that filters as you type highlights the name its text matches, for Enter or Tab to pick.
        PickerMatchHighlight.Register();

        // Every picker that's typed into selects its text as the keyboard enters it, by a click or by Tab.
        PickerSelectOnEntry.Register();

        // A picker emptied while bound to a value that can't be empty shows that value again once the keyboard leaves.
        PickerRestoresPick.Register();

        // Ctrl+Z and Ctrl+Y for the screen the cursor is on. It goes here, on every window, because Syncfusion's
        // number boxes take both keys for their own per-character undo and mark them handled on the way down.
        UndoShortcuts.Register();

        // The Vim keys, on every window for the same reason; they do nothing until turned on (OnStartup, Settings).
        VimKeys.Register();
    }

    /// <summary>
    /// Lines an SfTextBoxExt's text up with the other fields. The Windows11Light theme's SfTextBoxExt
    /// template puts a fixed 10px margin before its text box, where DoubleTextBox's uses 3px of
    /// padding, so Name's text started 13px in against 8px for a DoubleTextBox and 7px for a
    /// ComboBoxAdv. The template passes Padding (the theme's -1) to the text presenter inside that
    /// margin, so a left padding of -6 brings the text to 8px. It must be a local value: the theme
    /// sets Padding through the control's default style, which outranks a type default
    /// (OverrideMetadata) and which a SetCurrentValue at Loaded doesn't survive. A Padding set in
    /// XAML still wins.
    /// </summary>
    private static void AlignTextBoxText(object sender, RoutedEventArgs e)
    {
        var box = (SfTextBoxExt)sender;

        if (box.ReadLocalValue(Control.PaddingProperty) == DependencyProperty.UnsetValue)
        {
            var padding = box.Padding;
            box.Padding = new Thickness(-6, padding.Top, padding.Right, padding.Bottom);
        }
    }

    /// <summary>
    /// Right-aligns every number box's text (2026-09-28, at the user's request: "make textbox containing numeric values
    /// like percentage and double be right aligned"), as figures line up in a column and as the grids show them.
    /// EditorBase is what Syncfusion's DoubleTextBox, IntegerTextBox, CurrencyTextBox and PercentTextBox are built on,
    /// a plain TextBox underneath. A local value, as AlignTextBoxText's is, so the theme's style can't outrank it; a box
    /// whose XAML or grid already says where its text goes keeps that.
    /// </summary>
    private static void AlignNumberRight(object sender, RoutedEventArgs e)
    {
        var box = (EditorBase)sender;

        if (box.ReadLocalValue(TextBox.TextAlignmentProperty) == DependencyProperty.UnsetValue)
        {
            box.TextAlignment = TextAlignment.Right;
        }
    }

    /// <summary>
    /// Every picker filters its list as you type: editable, with suggestions. ComboBoxAdv owns both
    /// properties itself, so unlike SfTextBoxExt above their defaults can't be changed with
    /// OverrideMetadata, and a Style would replace the theme's. Instead each ComboBoxAdv gets them
    /// as it loads, unless its XAML sets them. SetCurrentValue keeps them defaults rather than local
    /// values, so a later Loaded (e.g. switching back to a tab) applies them again harmlessly.
    /// </summary>
    private static void ApplyPickerDefaults(object sender, RoutedEventArgs e)
    {
        var picker = (ComboBoxAdv)sender;

        if (picker.ReadLocalValue(ComboBoxAdv.IsEditableProperty) == DependencyProperty.UnsetValue)
        {
            picker.SetCurrentValue(ComboBoxAdv.IsEditableProperty, true);
        }

        if (picker.ReadLocalValue(ComboBoxAdv.AutoCompleteModeProperty) == DependencyProperty.UnsetValue)
        {
            picker.SetCurrentValue(ComboBoxAdv.AutoCompleteModeProperty, AutoCompleteModes.Suggest);
        }
    }

    /// <summary>
    /// Shows a grid's add row as a plain empty row. Until the row is edited, the Windows11Light theme covers it with a
    /// light-gray band carrying the add-row text ("Click here to add new row" when a grid sets none), which also hid
    /// the column lines. With the band collapsed only the empty cells show, and clicking one still starts a line. The
    /// band's Edit visual state collapses it too, so a local value holds in both states.
    /// </summary>
    private static void HideAddNewRowText(object sender, RoutedEventArgs e)
    {
        var row = (AddNewRowControl)sender;

        if (row.Template?.FindName("PART_AddNewRowTextBorder", row) is UIElement band)
        {
            band.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (!_singleInstance.Claim(SingleInstanceGuard.Patience, SingleInstanceGuard.OpenCopyElsewhere, ShowError))
        {
            Shutdown();
            return;
        }

        // Until the main window shows there's nothing to carry on in, so a failure says why and closes the app.
        if (!AppStartup.TryStart(() => ShowMainWindow(e.Args), LogFolder, Log.Logger, message => ShowError(message, MessageBoxImage.Error), Shutdown))
        {
            return;
        }

        // Only now: another copy started before this one's window showed has been waiting for it to come up.
        _singleInstance.Listen(() => Dispatcher.InvokeAsync(BringToFront));

        _ = WarnOfPendingMigrationsAsync(_host!.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>());
        // The day's automatic backup now, and checked again every hour, so a copy left open over midnight takes the next day's.
        var automaticBackups = _host.Services.GetRequiredService<AutomaticBackups>();
        _ = BackUpDailyAsync(automaticBackups);
        _backupTimer = new DispatcherTimer { Interval = AutomaticBackups.CheckEvery };
        _backupTimer.Tick += (_, _) => _ = BackUpDailyAsync(automaticBackups);
        _backupTimer.Start();
    }

    /// <summary>
    /// What another copy asks for as it closes. With the window gone, this one is on its way out, so it isn't answered for,
    /// and the copy that asked gets the app once this one has closed.
    /// </summary>
    private void BringToFront()
    {
        if (MainWindow is { IsVisible: true } window)
        {
            SingleInstanceGuard.BringToFront(new WindowInteropHelper(window).Handle);
            _singleInstance.CameForward();
        }
    }

    /// <summary>The theme, the host and the main window: everything the app needs before there's a window to show a failure in.</summary>
    private void ShowMainWindow(string[] args)
    {
        // Re-colors the theme's primary accent (buttons, grid selection, tab/drawer
        // indicators, focus rings) to the app's Catppuccin Latte Sky, with text on it in Sky's Ink
        // (white on Sky is only 2.8:1; see Themes/Palette.xaml). PrimaryBackground's setter
        // derives the Dark1-3/Light1-3/Opacity1-3 shades, so this one property is enough.
        //
        // ApplyThemeAsDefaultStyle is deliberately NOT set: turning it on makes SfSkinManager
        // re-style every open Window as a ChromelessWindow (see SfSkinManager.ApplyWindowStyle),
        // replacing the native title bar with Syncfusion's custom chrome. Without it, Syncfusion
        // controls still pick up the theme and accent via SetTheme below; only plain framework
        // controls (CheckBox, Expander, ProgressBar) keep the OS default look.
        SfSkinManager.RegisterThemeSettings(
            "Windows11Light",
            ThemeSettings(new SolidColorBrush((Color)Resources["LatteSkyColor"]), new SolidColorBrush((Color)Resources["LatteSkyInkColor"])));

        _host = CompositionRoot.CreateHostBuilder(args).Build();
        _host.Start();

        // On if this Windows user turned them on in Settings' Keyboard tab.
        VimKeys.State.IsEnabled = KeyboardSettingsViewModel.IsVimKeysSaved(_host.Services.GetRequiredService<IViewStateStore>());

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        SfSkinManager.SetTheme(mainWindow, new Theme("Windows11Light"));
        mainWindow.Show();
    }

    // Checks every hour whether the day's automatic backup is due (OnStartup).
    private DispatcherTimer? _backupTimer;

    /// <summary>
    /// The day's automatic backup, the first time the app starts each day (Settings → Database), off the UI thread once
    /// the window shows, so the app starts no later. A backup that couldn't be taken is logged and shown on the Database
    /// tab, and the next check tries again, since none was taken that day. It only interrupts when they've been failing
    /// long enough for the newest backup to have got old (AutomaticBackups.OverdueWarningAsync).
    /// </summary>
    private async Task BackUpDailyAsync(AutomaticBackups backups)
    {
        try
        {
            var warning = await Task.Run(async () =>
            {
                var now = DateTime.Now;
                await backups.RunAsync(now, CancellationToken.None);
                return await backups.OverdueWarningAsync(now, CancellationToken.None);
            });

            if (warning is not null)
            {
                ShowError(warning, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't take the day's automatic backup");
        }
    }

    /// <summary>
    /// Says <paramref name="message"/>, then closes the app and starts it again: after a restore, which replaces everything
    /// every screen holds. The new copy starts once this one has finished closing (OnExit), with its log written and closed
    /// and the app let go, or it would find this one still open and close again.
    /// </summary>
    public void RestartAfterExit(string message)
    {
        ShowError(message, MessageBoxImage.Information);
        _restartOnExit = true;
        Shutdown();
    }

    /// <summary>
    /// Says so when the database is behind this version of the app: a migration added since it was last updated, and not
    /// applied with `dotnet ef database update`. Every screen reading a column the database doesn't have yet would fail
    /// otherwise, one error notice at a time, with nothing saying why. Read off the UI thread once the window shows, so the
    /// app starts no later; a database that can't be reached is left to the screens, which say so themselves.
    /// </summary>
    private async Task WarnOfPendingMigrationsAsync(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory)
    {
        IReadOnlyList<string> pending;
        try
        {
            pending = await Task.Run(async () =>
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                return (IReadOnlyList<string>)[.. await dbContext.Database.GetPendingMigrationsAsync()];
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't check the database for migrations not applied yet");
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        Log.Warning("The database is missing {Count} migrations: {Migrations}", pending.Count, pending);
        ShowError(AppStartup.PendingMigrationsText(pending.Count), MessageBoxImage.Warning);
    }

    /// <summary>
    /// The app's Windows11Light settings: its accent, and its text sizes. Tests that theme a window register the same, so
    /// what they measure is what the app shows.
    /// </summary>
    public static Windows11LightThemeSettings ThemeSettings(Brush primaryBackground, Brush primaryForeground) => new()
    {
        PrimaryBackground = primaryBackground,
        PrimaryForeground = primaryForeground,
        // Each 2 above the theme's defaults (16/14/14/12/12/10), for readability (2026-09-27, at the
        // user's request). The themed windows take BodyFontSize as their FontSize, so plain TextBlocks
        // inherit it too.
        HeaderFontSize = 18,
        SubHeaderFontSize = 16,
        TitleFontSize = 16,
        SubTitleFontSize = 14,
        BodyFontSize = 14,
        BodyAltFontSize = 12,
    };

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
        _singleInstance.Dispose();

        if (_restartOnExit && Environment.ProcessPath is { } path)
        {
            Process.StartAndForget(new ProcessStartInfo(path) { WorkingDirectory = AppContext.BaseDirectory });
        }
    }

    /// <summary>
    /// An exception nothing else caught on the UI thread: logged, said in a message, and the app carries on with whatever
    /// hasn't been saved still there (UnhandledErrorGuard says when it shouldn't, and what to say).
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var exception = UnhandledErrorGuard.CauseOf(e.Exception);
        Log.Error(exception, "Unhandled exception on the UI thread");
        if (!_errorGuard.Admit(DateTime.Now))
        {
            Log.Fatal("Unhandled exceptions kept coming, more than {Count} in {Window}: closing", UnhandledErrorGuard.MaxFailures, UnhandledErrorGuard.Window);
            return;
        }

        e.Handled = true;
        ShowError(UnhandledErrorGuard.CarryOnText(exception), MessageBoxImage.Error);
    }

    /// <summary>A message over the main window. One at a time: another arriving while one is up is only logged.</summary>
    private void ShowError(string message, MessageBoxImage image)
    {
        if (_isShowingError)
        {
            return;
        }

        _isShowingError = true;
        try
        {
            if (MainWindow is { IsVisible: true } owner)
            {
                MessageBox.Show(owner, message, "TinapayanRMS", MessageBoxButton.OK, image);
            }
            else
            {
                MessageBox.Show(message, "TinapayanRMS", MessageBoxButton.OK, image);
            }
        }
        finally
        {
            _isShowingError = false;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception ({IsTerminating})", e.IsTerminating);
    }

    /// <summary>A task that failed with nothing awaiting it: nothing else would ever hear of it.</summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Warning(e.Exception, "A task failed with nothing awaiting it");
        e.SetObserved();
    }
}
