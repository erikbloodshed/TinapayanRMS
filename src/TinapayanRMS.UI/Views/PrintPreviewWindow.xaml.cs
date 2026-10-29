using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// A printout previewed and printed in a dialog (2026-10-03): the Production Plan's, or the Price Simulator report (2026-10-06),
/// neither of which is the Products screen's, so they don't go to the main window's Print tab. The window is themed before
/// the pane goes into it: a PrintPreview already in a window when SfSkinManager themes the window throws
/// (PrintPreviewThemeOrderTests), so whoever opens this mustn't theme it again.
/// </summary>
public sealed partial class PrintPreviewWindow : AppWindow
{
    /// <summary>One printout: the Production Plan's.</summary>
    public PrintPreviewWindow(ProductPrintSet set, Theme theme)
        : this([new PrintChoice(set.Title ?? "Print", set)], theme)
    {
    }

    /// <summary>
    /// <paramref name="choices"/>, the first showing, and a Print dropdown to show another when there's more than one: the
    /// Price Simulator report's selected products and all those shown. They're all the same kind of printout, under one title.
    /// </summary>
    public PrintPreviewWindow(IReadOnlyList<PrintChoice> choices, Theme theme)
    {
        InitializeComponent();
        var set = choices[0].Set;
        Title = set.Title ?? "Print";
        Heading.Text = $"Print: {Title}";
        SfSkinManager.SetTheme(this, theme);

        // A plan has only its costs to leave out; products have their recipe and their costing.
        var isPlan = set.Plan is not null;
        PlanCosts.Visibility = isPlan ? Visibility.Visible : Visibility.Collapsed;
        ProductParts.Visibility = isPlan ? Visibility.Collapsed : Visibility.Visible;

        // Both go with the window: disposed when it closes.
        var manager = new ProductPrintManager(set);
        var preview = new PrintPreview(manager);
        PrintPaneFit.Apply(preview);
        PreviewHost.Content = preview;
        IncludeBar.DataContext = manager;
        manager.PrintFailed += PrintFailed;

        // Another choice: its pages laid out on the paper, margins and orientation already picked, from their first, as
        // the Print tab shows another set (ProductPrintView), once they're laid out.
        void ChoiceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ChoiceBox.SelectedItem is not PrintChoice choice || ReferenceEquals(manager.PrintSet, choice.Set))
            {
                return;
            }

            manager.PrintSet = choice.Set;
            if (preview.Template?.FindName("PART_PrintPreviewAreaControl", preview) is PrintPreviewAreaControl area)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (area.FirstCommand.CanExecute(null))
                    {
                        area.FirstCommand.Execute(null);
                    }
                });
            }
        }

        if (choices.Count > 1)
        {
            ChoiceBar.Visibility = Visibility.Visible;
            ChoiceBox.ItemsSource = choices;
            ChoiceBox.SelectedIndex = 0;
            ChoiceBox.SelectionChanged += ChoiceChanged;
        }

        Closed += (_, _) =>
        {
            ChoiceBox.SelectionChanged -= ChoiceChanged;
            manager.PrintFailed -= PrintFailed;
            preview.Dispose();
            manager.Dispose();
        };
    }

    // The pane's Print button reports nothing when a print fails, so the window does.
    private void PrintFailed(object? sender, Exception exception) =>
        MessageBox.Show(this, $"The printout couldn't be sent to the printer. {exception.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Error);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
