using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;
using Syncfusion.Themes.Windows11Light.WPF;
using Syncfusion.Windows.Controls.Input;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Views;

namespace TinapayanRMS.UI;

public partial class App : Application
{
    private IHost? _host;

    static App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "tinapayanrms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31)
            .CreateLogger();

        try
        {
            var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
                ?? throw new InvalidOperationException("The SYNCFUSION_LICENSE_KEY environment variable is not set.");

            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed before the main window could be created");
            throw;
        }

        // The Windows11Light theme's SfTextBoxExt style — unlike its ComboBoxAdv, DoubleTextBox and
        // DateTimeEdit styles — never sets VerticalContentAlignment, so text falls back to
        // Control's default of Top and sits against the top of the box at the 32px
        // SizeMode="Touch" height. Changing the type's default here fixes every instance,
        // including future ones, without a Style on the control. Must run before any
        // SfTextBoxExt exists, which is why it lives in this static constructor.
        Control.VerticalContentAlignmentProperty.OverrideMetadata(
            typeof(SfTextBoxExt), new FrameworkPropertyMetadata(VerticalAlignment.Center));

        EventManager.RegisterClassHandler(
            typeof(SfTextBoxExt), FrameworkElement.LoadedEvent, new RoutedEventHandler(AlignTextBoxText));

        EventManager.RegisterClassHandler(
            typeof(ComboBoxAdv), FrameworkElement.LoadedEvent, new RoutedEventHandler(ApplyPickerDefaults));
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Re-colors the theme's primary accent (buttons, grid selection, tab/drawer
        // indicators, focus rings) to the app's warm amber. PrimaryBackground's setter
        // derives the Dark1-3/Light1-3/Opacity1-3 shades, so this one property is enough.
        //
        // ApplyThemeAsDefaultStyle is deliberately NOT set: turning it on makes SfSkinManager
        // re-style every open Window as a ChromelessWindow (see SfSkinManager.ApplyWindowStyle),
        // replacing the native title bar with Syncfusion's custom chrome. Without it, Syncfusion
        // controls still pick up the theme and accent via SetTheme below; only plain framework
        // controls (CheckBox, Expander, ProgressBar) keep the OS default look.
        SfSkinManager.RegisterThemeSettings("Windows11Light", new Windows11LightThemeSettings
        {
            PrimaryBackground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)),
            PrimaryForeground = Brushes.White,
        });

        _host = CompositionRoot.CreateHostBuilder(e.Args).Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        SfSkinManager.SetTheme(mainWindow, new Theme("Windows11Light"));
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception ({IsTerminating})", e.IsTerminating);
    }
}
