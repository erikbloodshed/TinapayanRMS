using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// Find (Ctrl+K, or the title bar's magnifier): a box over the names of every raw material, standalone sub-assembly and
/// product, and the match picked. The keyboard stays in the box: Up and Down move the pick, Enter opens it, Esc closes.
/// MainWindow opens what's picked on its own screen. Chromeless, as TextPromptWindow is.
/// </summary>
public partial class QuickFindWindow : Window
{
    private readonly QuickFindViewModel _viewModel;

    private QuickFindWindow(QuickFindViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        // The last search is still there, selected, so typing replaces it and Enter opens its best match again.
        QueryTextBox.Loaded += (_, _) =>
        {
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
        };
    }

    /// <summary>The match picked, or null when Find was closed without one.</summary>
    public static QuickFindResult? Pick(QuickFindViewModel viewModel, Window owner)
    {
        var window = new QuickFindWindow(viewModel) { Owner = owner };

        // SfSkinManager themes only the windows it's given (TextPromptWindow says more).
        SfSkinManager.SetTheme(window, SfSkinManager.GetTheme(owner));
        viewModel.LoadCommand.Execute().Subscribe();
        return window.ShowDialog() == true ? viewModel.SelectedResult : null;
    }

    private void QueryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            Key.PageDown => 10,
            Key.PageUp => -10,
            _ => 0,
        };
        if (step == 0 || _viewModel.Results.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(_viewModel.Results.IndexOf(_viewModel.SelectedResult!) + step, 0, _viewModel.Results.Count - 1);
        _viewModel.SelectedResult = _viewModel.Results[index];
        ResultsGrid.ScrollInView(new Syncfusion.UI.Xaml.ScrollAxis.RowColumnIndex(ResultsGrid.ResolveToRowIndex(index), 0));
        e.Handled = true;
    }

    // Enter on a row the mouse put the keyboard on opens it, as it does in the box, rather than moving down a row.
    private void ResultsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Open();
            e.Handled = true;
        }
    }

    private void ResultsGrid_CellDoubleTapped(object? sender, GridCellDoubleTappedEventArgs e)
    {
        if (e.Record is QuickFindResult result)
        {
            _viewModel.SelectedResult = result;
            Open();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => Open();

    private void Open()
    {
        if (_viewModel.SelectedResult is not null)
        {
            DialogResult = true;
        }
    }

    // ButtonAdv.IsCancel only wires Esc to a Click; it doesn't set DialogResult itself (TextPromptWindow says more).
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Heading_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
