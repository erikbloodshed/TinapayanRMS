using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// What the Vim keys draw over a window: the link hints' labels, the command line along the bottom, and the cheat sheet
/// in the middle. One adorner on the window content's adorner layer, so no screen's XAML knows of it and it works in any
/// window the app opens. Each part is built the first time it's asked for and then shown and hidden.
/// </summary>
internal sealed class VimOverlay : Adorner
{
    private static readonly ConditionalWeakTable<Window, VimOverlay> Overlays = new();

    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly VisualCollection _children;
    private HintLayer? _hints;
    private CommandBar? _commandBar;
    private CheatSheet? _cheatSheet;

    // Where the keyboard was when the command line opened, and what the keys there are, for :help.
    private IInputElement? _returnFocus;
    private VimContexts _context = VimContexts.Other;

    private VimOverlay(FrameworkElement content, Window window)
        : base(content)
    {
        _window = window;
        _content = content;
        _children = new VisualCollection(this);
    }

    /// <summary>The overlay taking the keys now — its hints or its cheat sheet are up — or null.</summary>
    public static VimOverlay? Capturing { get; private set; }

    public bool IsShowingHints => _hints is { Visibility: Visibility.Visible };

    /// <summary>Whether this is <paramref name="window"/>'s overlay.</summary>
    public bool IsFor(object window) => ReferenceEquals(window, _window);

    /// <summary><paramref name="window"/>'s overlay, made on first use. Null if its content has no adorner layer to go in.</summary>
    public static VimOverlay? For(Window window)
    {
        if (Overlays.TryGetValue(window, out var overlay))
        {
            return overlay;
        }

        if (window.Content is not FrameworkElement content || AdornerLayer.GetAdornerLayer(content) is not { } layer)
        {
            Log.Warning("The Vim keys can't draw over {Window}: its content has no adorner layer", window.GetType().Name);
            return null;
        }

        overlay = new VimOverlay(content, window);
        layer.Add(overlay);
        Overlays.Add(window, overlay);

        // Hints and the cheat sheet are for the keyboard; the mouse, or another window, puts them away.
        window.PreviewMouseDown += (_, _) => overlay.CloseCapture();
        window.Deactivated += (_, _) => overlay.CloseCapture();
        return overlay;
    }

    /// <summary><c>f</c>: a label on everything in view that can be clicked. Typing one acts on it.</summary>
    public void ShowHints()
    {
        CloseCapture();
        var targets = HintTargets.In(_content);
        if (targets.Count == 0)
        {
            return;
        }

        _hints ??= Attach(new HintLayer());
        _hints.Show(targets);
        Capturing = this;
    }

    /// <summary><c>?</c>: the keys that work where the keyboard is.</summary>
    public void ShowCheatSheet(VimContexts context)
    {
        CloseCapture();
        _cheatSheet ??= Attach(new CheatSheet());
        _cheatSheet.Show(context);
        Capturing = this;
    }

    /// <summary><c>:</c>: the command line, with the keyboard in it.</summary>
    public void ShowCommandLine(DependencyObject? from, VimContexts context) => ShowCommandLine(from as IInputElement, context, string.Empty, string.Empty);

    private void ShowCommandLine(IInputElement? returnFocus, VimContexts context, string text, string message)
    {
        CloseCapture();
        _returnFocus = returnFocus;
        _context = context;
        _commandBar ??= Attach(new CommandBar(this));
        _commandBar.Show(text, message);
    }

    /// <summary>A key pressed while the hints or the cheat sheet are up. True when it's taken.</summary>
    public bool TakeKey(Key key, bool typesTextOrModifier)
    {
        // A letter comes on as text (TakeText); Shift on its own changes nothing.
        if (typesTextOrModifier)
        {
            return false;
        }

        if (key == Key.Back && IsShowingHints)
        {
            _hints!.Back();
            return true;
        }

        if (_cheatSheet is { Visibility: Visibility.Visible } && key is Key.Down or Key.Up or Key.PageDown or Key.PageUp)
        {
            _cheatSheet.Scroll(key);
            return true;
        }

        // Esc puts them away; any other key does too, and goes on to do what it does.
        CloseCapture();
        return key == Key.Escape;
    }

    /// <summary>A character typed while the hints or the cheat sheet are up.</summary>
    public void TakeText(char key)
    {
        if (IsShowingHints)
        {
            if (_hints!.Type(key) is { } picked)
            {
                CloseCapture();
                picked.Activate();
                VimKeys.ShowMode();
            }

            return;
        }

        switch (key)
        {
            case '?' or 'q':
                CloseCapture();
                break;
            case 'j':
                _cheatSheet?.Scroll(Key.Down);
                break;
            case 'k':
                _cheatSheet?.Scroll(Key.Up);
                break;
        }
    }

    /// <summary>Puts the hints and the cheat sheet away. The command line closes itself.</summary>
    public void CloseCapture()
    {
        if (_hints is not null)
        {
            _hints.Visibility = Visibility.Collapsed;
        }

        if (_cheatSheet is not null)
        {
            _cheatSheet.Visibility = Visibility.Collapsed;
        }

        if (ReferenceEquals(Capturing, this))
        {
            Capturing = null;
            VimKeys.ShowMode();
        }
    }

    /// <summary>Enter on the command line: runs what's typed, or says why it can't.</summary>
    private void Run(string text)
    {
        var command = VimCommandLine.Parse(text, out var error);
        if (command is null)
        {
            _commandBar!.Show(text, error);
            return;
        }

        // The keyboard goes back where it was first, so the command runs from there, as the key would have.
        HideCommandLine(restoreFocus: true);
        if (command.Kind == VimCommandKind.Help)
        {
            ShowCheatSheet(_context);
            return;
        }

        if (VimCommands.Run(command, _window, _returnFocus as DependencyObject) is { } failure)
        {
            ShowCommandLine(_returnFocus, _context, text, failure);
        }
    }

    private void HideCommandLine(bool restoreFocus)
    {
        if (_commandBar is not { Visibility: Visibility.Visible })
        {
            return;
        }

        _commandBar.Visibility = Visibility.Collapsed;
        if (restoreFocus && _returnFocus is UIElement { IsVisible: true } element)
        {
            VimKeys.FocusInNormal(element);
        }
        else if (restoreFocus)
        {
            _content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }

        VimKeys.ShowMode();
    }

    private T Attach<T>(T part)
        where T : UIElement
    {
        _children.Add(part);
        InvalidateMeasure();
        return part;
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size constraint)
    {
        var size = AdornedElement.RenderSize;
        foreach (UIElement child in _children)
        {
            // The cheat sheet stops short of the window's edges, and scrolls what doesn't fit.
            child.Measure(child is CheatSheet ? new Size(size.Width * 0.9, size.Height * 0.85) : size);
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in _children)
        {
            switch (child)
            {
                case CommandBar bar:
                    bar.Arrange(new Rect(0, finalSize.Height - bar.DesiredSize.Height, finalSize.Width, bar.DesiredSize.Height));
                    break;
                case CheatSheet sheet:
                    var width = Math.Min(sheet.DesiredSize.Width, finalSize.Width);
                    var height = Math.Min(sheet.DesiredSize.Height, finalSize.Height);
                    sheet.Arrange(new Rect((finalSize.Width - width) / 2, (finalSize.Height - height) / 2, width, height));
                    break;
                default:
                    child.Arrange(new Rect(finalSize));
                    break;
            }
        }

        return finalSize;
    }

    private static Brush BrushFor(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    /// <summary>
    /// The hints' labels, all drawn by this one element in OnRender: no element per label, so a screenful shows at once.
    /// Each label's text is made once and kept for the next time.
    /// </summary>
    private sealed class HintLayer : FrameworkElement
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private readonly Dictionary<string, FormattedText> _texts = [];
        private readonly List<(HintTarget Target, string Label)> _shown = [];
        private readonly Brush _background = BrushFor("WarningBackgroundBrush", Brushes.LightYellow);
        private readonly Pen _border = new(BrushFor("WarningForegroundBrush", Brushes.DarkGoldenrod), 1);
        private readonly Brush _foreground = BrushFor("HeadingForegroundBrush", Brushes.Black);
        private readonly Brush _typedForeground = BrushFor("LatteOverlay0Brush", Brushes.Gray);
        private double _pixelsPerDip;
        private string _typed = string.Empty;

        public HintLayer()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
        }

        public void Show(List<HintTarget> targets)
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (pixelsPerDip != _pixelsPerDip)
            {
                _texts.Clear();
                _pixelsPerDip = pixelsPerDip;
            }

            var labels = HintLabels.For(targets.Count);
            _shown.Clear();
            for (var i = 0; i < targets.Count; i++)
            {
                _shown.Add((targets[i], labels[i]));
            }

            _typed = string.Empty;
            Visibility = Visibility.Visible;
            InvalidateVisual();
        }

        /// <summary>Takes a typed character. The target when it completes a label; null while more is needed, or when
        /// no label goes on that way (the character is then ignored).</summary>
        public HintTarget? Type(char key)
        {
            var typed = _typed + char.ToLowerInvariant(key);
            HintTarget? whole = null;
            var matches = 0;
            foreach (var (target, label) in _shown)
            {
                if (label.StartsWith(typed, StringComparison.Ordinal))
                {
                    matches++;
                    if (label.Length == typed.Length)
                    {
                        whole = target;
                    }
                }
            }

            if (matches == 0)
            {
                return null;
            }

            _typed = typed;
            InvalidateVisual();
            return whole;
        }

        public void Back()
        {
            if (_typed.Length > 0)
            {
                _typed = _typed[..^1];
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            foreach (var (target, label) in _shown)
            {
                if (!label.StartsWith(_typed, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = TextFor(label);
                text.SetForegroundBrush(_foreground);
                if (_typed.Length > 0)
                {
                    text.SetForegroundBrush(_typedForeground, 0, _typed.Length);
                }

                var box = new Rect(target.Position.X, target.Position.Y, text.Width + 8, text.Height + 2);
                drawingContext.DrawRoundedRectangle(_background, _border, box, 3, 3);
                drawingContext.DrawText(text, new Point(box.X + 4, box.Y + 1));
            }
        }

        private FormattedText TextFor(string label)
        {
            if (!_texts.TryGetValue(label, out var text))
            {
                text = new FormattedText(
                    label.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 12, _foreground, _pixelsPerDip);
                _texts[label] = text;
            }

            return text;
        }
    }

    /// <summary>The command line: a <c>:</c> and a box along the bottom of the window, and a line for what went wrong.</summary>
    private sealed class CommandBar : Border
    {
        private static readonly FontFamily Mono = new("Consolas");

        private readonly TextBox _box;
        private readonly TextBlock _message;

        public CommandBar(VimOverlay overlay)
        {
            Visibility = Visibility.Collapsed;
            Background = BrushFor("CardBackgroundBrush", Brushes.White);
            BorderBrush = BrushFor("CardBorderBrush", Brushes.LightGray);
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(12, 6, 12, 6);

            var prompt = new TextBlock { Text = ":", FontFamily = Mono, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            _box = new TextBox
            {
                FontFamily = Mono,
                FontSize = 14,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _message = new TextBlock
            {
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = BrushFor("ErrorForegroundBrush", Brushes.DarkRed),
            };

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_box, 1);
            Grid.SetColumn(_message, 2);
            layout.Children.Add(prompt);
            layout.Children.Add(_box);
            layout.Children.Add(_message);
            Child = layout;

            _box.PreviewKeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        e.Handled = true;
                        overlay.Run(_box.Text);
                        break;
                    case Key.Escape:
                        e.Handled = true;
                        overlay.HideCommandLine(restoreFocus: true);
                        break;
                }
            };

            // A click elsewhere puts the line away, leaving the keyboard where the click put it.
            _box.LostKeyboardFocus += (_, _) => overlay.HideCommandLine(restoreFocus: false);
        }

        public void Show(string text, string message)
        {
            _box.Text = text;
            _message.Text = message;
            Visibility = Visibility.Visible;
            UpdateLayout();
            _box.Focus();
            _box.SelectAll();
        }
    }

    /// <summary>The keys that work where the keyboard is, then those that work anywhere, then the commands.</summary>
    private sealed class CheatSheet : Border
    {
        private static readonly FontFamily Mono = new("Consolas");

        private readonly Dictionary<VimContexts, UIElement> _pages = [];
        private readonly ScrollViewer _scroller;

        public CheatSheet()
        {
            Visibility = Visibility.Collapsed;
            Background = BrushFor("CardBackgroundBrush", Brushes.White);
            BorderBrush = BrushFor("CardBorderBrush", Brushes.LightGray);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(20, 16, 20, 16);
            MaxWidth = 640;
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 };
            _scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Child = _scroller;
        }

        public void Show(VimContexts context)
        {
            if (!_pages.TryGetValue(context, out var page))
            {
                page = Page(context);
                _pages[context] = page;
            }

            _scroller.Content = page;
            _scroller.ScrollToTop();
            Visibility = Visibility.Visible;
        }

        public void Scroll(Key key)
        {
            switch (key)
            {
                case Key.Down:
                    _scroller.LineDown();
                    break;
                case Key.Up:
                    _scroller.LineUp();
                    break;
                case Key.PageDown:
                    _scroller.PageDown();
                    break;
                case Key.PageUp:
                    _scroller.PageUp();
                    break;
            }
        }

        private static StackPanel Page(VimContexts context)
        {
            // Clear of the scroll bar, which the theme lays over the content.
            var page = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            page.Children.Add(new TextBlock
            {
                Text = "Vim keys",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFor("HeadingForegroundBrush", Brushes.Black),
            });
            page.Children.Add(Caption("The keys in Normal mode. Esc, q or ? closes this list, and j and k scroll it. In a field or cell being typed into, Esc stops typing and keeps what's typed."));

            var here = VimKeymap.For(context).Where(binding => binding.Where != VimContexts.Anywhere).ToList();
            if (here.Count > 0)
            {
                page.Children.Add(Heading(context switch
                {
                    VimContexts.Field => "In a field",
                    VimContexts.Tree => "In the Products or Raw Materials list",
                    VimContexts.Table => "In a table or list",
                    _ => "On a button or tab",
                }));
                page.Children.Add(Table(here.Select(binding => (binding.Keys, binding.Description))));
            }

            page.Children.Add(Heading("Anywhere"));
            page.Children.Add(Table(VimKeymap.Bindings.Where(binding => binding.Where == VimContexts.Anywhere).Select(binding => (binding.Keys, binding.Description))));

            page.Children.Add(Heading("Commands (type : first, then Enter)"));
            page.Children.Add(Table(VimCommandLine.Commands.Select(command => (command.Usage, command.Description))));
            return page;
        }

        private static TextBlock Heading(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 6),
            Foreground = BrushFor("HeadingForegroundBrush", Brushes.Black),
        };

        private static TextBlock Caption(string text) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = BrushFor("MutedForegroundBrush", Brushes.Gray),
        };

        private static Grid Table(IEnumerable<(string Keys, string Description)> rows)
        {
            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (var (keys, description) in rows)
            {
                var row = table.RowDefinitions.Count;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var key = new TextBlock { Text = keys, FontFamily = Mono, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 12, 2) };
                var meaning = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(key, row);
                Grid.SetRow(meaning, row);
                Grid.SetColumn(meaning, 1);
                table.Children.Add(key);
                table.Children.Add(meaning);
            }

            return table;
        }
    }
}
