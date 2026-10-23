using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private static readonly TimeSpan MessageShownFor = TimeSpan.FromSeconds(5);

    private readonly Window _window;
    private readonly FrameworkElement _content;
    private readonly VisualCollection _children;
    private HintLayer? _hints;
    private CommandBar? _commandBar;
    private CheatSheet? _cheatSheet;

    // Where the keyboard was when the command line opened, and what the keys there are, for :help.
    private IInputElement? _returnFocus;
    private VimContexts _context = VimContexts.Other;

    // Puts a command's message away when no key or click has first.
    private DispatcherTimer? _messageTimer;

    // Ctrl+R =: the sum typed so far, the box the answer is typed into, and the grid it's a cell of (or null).
    private string _sum = string.Empty;
    private DependencyObject? _answerTarget;
    private Syncfusion.UI.Xaml.Grid.SfDataGrid? _answerGrid;

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

    /// <summary>Whether the line along the bottom is taking a sum (Ctrl+R =).</summary>
    public bool IsShowingExpression => _commandBar is { IsShowingExpression: true };

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

        // Hints and the cheat sheet are for the keyboard; the mouse, or another window, puts them away. A command's
        // message goes with them, and with the next key, which goes on to do what it does.
        window.PreviewMouseDown += (_, _) => overlay.CloseCapture();
        window.Deactivated += (_, _) => overlay.CloseCapture();
        window.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, _) => overlay.HideMessage()), handledEventsToo: true);
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
    public void ShowCommandLine(DependencyObject? from, VimContexts context)
    {
        CloseCapture();
        _returnFocus = from as IInputElement;
        _context = context;
        _commandBar ??= Attach(new CommandBar(this));
        _commandBar.Show();
    }

    /// <summary>
    /// Ctrl+R = in Insert: a <c>=</c> along the bottom to type a sum after, as Vim's expression register. The keyboard
    /// stays where it is, in <paramref name="target"/>: moving it to a box down there would end a cell's edit
    /// (GridEditCommit) and lose the caret. So, as with the hints, the keys come here (TakeKey, TakeText) until Enter
    /// types the answer into <paramref name="target"/>, or Esc, a click or another window puts the line away.
    /// </summary>
    public void ShowExpressionLine(DependencyObject? target, Syncfusion.UI.Xaml.Grid.SfDataGrid? grid)
    {
        CloseCapture();
        _sum = string.Empty;
        _answerTarget = target;
        _answerGrid = grid;
        _commandBar ??= Attach(new CommandBar(this));
        _commandBar.ShowExpression(_sum);
        Capturing = this;
        VimKeys.ShowMode();
    }

    /// <summary>A key pressed while the hints or the cheat sheet are up. True when it's taken.</summary>
    public bool TakeKey(Key key, bool typesTextOrModifier)
    {
        if (IsShowingExpression)
        {
            return TakeSumKey(key, typesTextOrModifier);
        }

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
        // A space comes as a key (TakeSumKey): a number box takes Space on its key press, so no text follows it there.
        if (IsShowingExpression)
        {
            if (key != ' ')
            {
                SetSum(_sum + key);
            }

            return;
        }

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

    /// <summary>Puts the hints, the cheat sheet and a command's message away. The command line closes itself.</summary>
    public void CloseCapture()
    {
        HideMessage();
        if (_hints is not null)
        {
            _hints.Visibility = Visibility.Collapsed;
        }

        if (_cheatSheet is not null)
        {
            _cheatSheet.Visibility = Visibility.Collapsed;
        }

        if (_commandBar is { IsShowingExpression: true })
        {
            _commandBar.Visibility = Visibility.Collapsed;
        }

        _answerTarget = null;
        _answerGrid = null;

        if (ReferenceEquals(Capturing, this))
        {
            Capturing = null;
            VimKeys.ShowMode();
        }
    }

    /// <summary>
    /// Enter on the command line: closes it and runs what's typed, then says in its place why it couldn't, as Vim does.
    /// </summary>
    private void Run(string text)
    {
        // The keyboard goes back where it was first, so the command runs from there, as the key would have.
        HideCommandLine(restoreFocus: true);
        var command = VimCommandLine.Parse(text, out var error);
        if (command is null)
        {
            ShowMessage(error);
            return;
        }

        if (command.Kind == VimCommandKind.Help)
        {
            ShowCheatSheet(_context);
            return;
        }

        if (VimCommands.Run(command, _window, _returnFocus as DependencyObject) is { } failure)
        {
            ShowMessage(failure);
        }
    }

    /// <summary>A line along the bottom, where the command line was, until the next key or click, or a few seconds pass.</summary>
    private void ShowMessage(string message)
    {
        _commandBar!.ShowMessage(message);
        if (_messageTimer is null)
        {
            _messageTimer = new DispatcherTimer { Interval = MessageShownFor };
            _messageTimer.Tick += (_, _) => HideMessage();
        }

        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void HideMessage()
    {
        _messageTimer?.Stop();
        if (_commandBar is { IsShowingMessage: true, Visibility: Visibility.Visible })
        {
            _commandBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// A key pressed while a sum is typed: Enter works it out, Esc puts the line away, Backspace takes a character off,
    /// Ctrl+V pastes. Every other key is taken and does nothing, so an arrow or Tab can't move the cell from under it.
    /// A character comes on as text (TakeText), except a space.
    /// </summary>
    private bool TakeSumKey(Key key, bool typesTextOrModifier)
    {
        if (key == Key.Space)
        {
            SetSum(_sum + ' ');
            return true;
        }

        if (typesTextOrModifier)
        {
            return false;
        }

        switch (key)
        {
            case Key.Enter:
                RunSum();
                break;
            case Key.Escape:
                CloseCapture();
                break;
            case Key.Back when _sum.Length > 0:
                SetSum(_sum[..^1]);
                break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.Control:
                SetSum(_sum + PastedLine());
                break;
        }

        return true;
    }

    private void SetSum(string sum)
    {
        _sum = sum;
        _commandBar?.ShowExpression(sum);
    }

    /// <summary>
    /// Enter on a sum: works it out and types the answer into the box the keyboard is in. A sum that can't be worked out
    /// keeps the line up, saying why, to be put right or put away.
    /// </summary>
    private void RunSum()
    {
        if (VimExpression.Evaluate(_sum, out var error) is not { } answer)
        {
            _commandBar!.ShowExpression(_sum, error);
            return;
        }

        var target = _answerTarget;
        var grid = _answerGrid;
        CloseCapture();

        // Enter is still on its way to the window, whose own handler puts a message away, so one is shown after it.
        if (VimKeys.TypeAnswer(target, grid, answer) is { } failure)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ShowMessage(failure));
        }
    }

    /// <summary>The clipboard's text up to its first line break, or nothing when it holds none or can't be read.</summary>
    private static string PastedLine()
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            var lineBreak = text.IndexOfAny(['\r', '\n']);
            return lineBreak < 0 ? text : text[..lineBreak];
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return string.Empty;
        }
    }

    private void HideCommandLine(bool restoreFocus)
    {
        if (_commandBar is not { IsShowingCommand: true })
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

    /// <summary>
    /// The command line: a <c>:</c> and a box along the bottom of the window. Once a command has run, the same bar shows
    /// on its own what went wrong, with no box, and the keyboard back where it was. For Ctrl+R = it shows a <c>=</c> and
    /// the sum typed so far instead, drawn as text, not in the box: the keyboard stays in the cell the answer goes into.
    /// </summary>
    private sealed class CommandBar : Border
    {
        private static readonly FontFamily Mono = new("Consolas");

        private readonly TextBlock _prompt;
        private readonly TextBox _box;
        private readonly TextBlock _message;
        private readonly TextBlock _expression;
        private readonly Run _typed = new();
        private readonly Run _error = new();

        public CommandBar(VimOverlay overlay)
        {
            Visibility = Visibility.Collapsed;
            Background = BrushFor("CardBackgroundBrush", Brushes.White);
            BorderBrush = BrushFor("CardBorderBrush", Brushes.LightGray);
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(12, 6, 12, 6);

            _prompt = new TextBlock { Text = ":", FontFamily = Mono, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
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
                FontFamily = Mono,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = BrushFor("ErrorForegroundBrush", Brushes.DarkRed),
            };

            // The sum, a bar where the next character goes (no box has the keyboard to draw a caret), and what's wrong.
            _error.Foreground = BrushFor("ErrorForegroundBrush", Brushes.DarkRed);
            _expression = new TextBlock
            {
                FontFamily = Mono,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed,
            };
            _expression.Inlines.Add(_typed);
            _expression.Inlines.Add(new Run("▏") { Foreground = BrushFor("MutedForegroundBrush", Brushes.Gray) });
            _expression.Inlines.Add(_error);

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_box, 1);
            Grid.SetColumn(_expression, 1);
            Grid.SetColumnSpan(_message, 2);
            layout.Children.Add(_prompt);
            layout.Children.Add(_box);
            layout.Children.Add(_expression);
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

        private enum Part
        {
            Command,
            Expression,
            Message,
        }

        /// <summary>Whether it's showing a command's message rather than taking one.</summary>
        public bool IsShowingMessage => _message.Visibility == Visibility.Visible;

        /// <summary>Whether it's up and taking a command after <c>:</c>.</summary>
        public bool IsShowingCommand => Visibility == Visibility.Visible && _box.Visibility == Visibility.Visible;

        /// <summary>Whether it's up and taking a sum after Ctrl+R =.</summary>
        public bool IsShowingExpression => Visibility == Visibility.Visible && _expression.Visibility == Visibility.Visible;

        public void Show()
        {
            _box.Text = string.Empty;
            SetShowing(Part.Command);
            Visibility = Visibility.Visible;
            UpdateLayout();
            _box.Focus();
        }

        /// <summary>The sum typed so far after a <c>=</c>, and why it can't be worked out when Enter found it couldn't.</summary>
        public void ShowExpression(string typed, string error = "")
        {
            _typed.Text = typed;
            _error.Text = error.Length == 0 ? string.Empty : "   " + error;
            SetShowing(Part.Expression);
            Visibility = Visibility.Visible;
        }

        /// <summary>Shows <paramref name="message"/> in place of the box. Nothing in it takes the keyboard or the mouse.</summary>
        public void ShowMessage(string message)
        {
            _message.Text = message;
            SetShowing(Part.Message);
            Visibility = Visibility.Visible;
        }

        private void SetShowing(Part part)
        {
            _prompt.Text = part == Part.Expression ? "=" : ":";
            _prompt.Visibility = part == Part.Message ? Visibility.Collapsed : Visibility.Visible;
            _box.Visibility = part == Part.Command ? Visibility.Visible : Visibility.Collapsed;
            _expression.Visibility = part == Part.Expression ? Visibility.Visible : Visibility.Collapsed;
            _message.Visibility = part == Part.Message ? Visibility.Visible : Visibility.Collapsed;

            // Only the command line's box is clicked into; the sum is typed with the keyboard left in its cell.
            IsHitTestVisible = part == Part.Command;
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
