using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Serilog;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using Syncfusion.UI.Xaml.TreeGrid;
using Syncfusion.UI.Xaml.TreeView;
using Syncfusion.UI.Xaml.TreeView.Engine;
using Syncfusion.Windows.Controls.Input;
using Syncfusion.Windows.Shared;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.UI.Vim;

/// <summary>
/// Vim keys for moving round the app and running its commands, in the manner of Vimium: in Normal mode the letters move
/// and act, and in Insert mode — a field or a grid cell being typed into — every key types, as it always did. Off unless
/// turned on in Settings (<see cref="State"/>), and then nothing at all changes.
/// <para>
/// <b>Where the keys are caught.</b> Class handlers on Window, as <see cref="UndoShortcuts"/>' are, so they run before
/// any control, Syncfusion's number boxes included. A letter is read from <c>PreviewTextInput</c>, where it's already
/// the character the keyboard layout makes: a grid starts a cell edit for a letter from its <c>OnTextInput</c>
/// (<c>char.IsLetterOrDigit</c>, then <c>BeginEdit</c>), which this beats. Keys that type nothing (Esc, Ctrl+R) come
/// through <c>PreviewKeyDown</c>.
/// </para>
/// <para>
/// <b>How a key acts.</b> In a grid or a list a key mostly stands for the key it replaces (<c>j</c> is Down, <c>dd</c>
/// Delete), raised on the element that has the keyboard as a real press is: PreviewKeyDown, then KeyDown unless that
/// was handled, which is what WPF's KeyboardDevice does. So the grid, the tree and the app's own behaviours on them
/// (GridFillerRows, GridUndoSteps, the lists' F2/Del/Enter) do exactly what they do for the key. Only keys typed without
/// a modifier are raised: a raised key reads Ctrl and Shift from the keyboard itself, so <c>G</c> raised as End would
/// arrive as Shift+End and pick a range in the Extended lists. <c>gg</c>, <c>G</c>, <c>0</c> and <c>$</c> use the
/// grid's own moves instead, and <c>G</c> in a list picks its last row. In a form, <c>j</c> and <c>k</c> move between
/// fields: arrows would step a number box's value.
/// </para>
/// <para>
/// <b>Fast.</b> Every key in the app passes through here, so where the keyboard is (<see cref="Place"/>) is worked out
/// once per focus change, not per key, and a key is a table lookup and the action itself, done there and then.
/// </para>
/// </summary>
public static class VimKeys
{
    /// <summary>Whether the keys are on, their mode, and the key typed so far: what the title bar shows.</summary>
    public static VimState State { get; } = new();

    /// <summary>Marks a screen's list search, which <c>/</c> puts the keyboard in.</summary>
    public static readonly DependencyProperty IsSearchBoxProperty = DependencyProperty.RegisterAttached(
        "IsSearchBox", typeof(bool), typeof(VimKeys), new PropertyMetadata(false, OnIsSearchBoxChanged));

    public static bool GetIsSearchBox(DependencyObject element) => (bool)element.GetValue(IsSearchBoxProperty);

    public static void SetIsSearchBox(DependencyObject element, bool value) => element.SetValue(IsSearchBoxProperty, value);

    private static readonly VimKeySequence Sequence = new();
    private static readonly List<WeakReference<TextBoxBase>> SearchBoxes = [];

    private static bool _registered;

    // Where the keyboard is and what the keys are doing there, per UI thread: the app has one, but tests run windows on
    // several at once, and none may read another's elements.

    // Set while a key is raised on a control, so the handlers here leave it alone.
    [ThreadStatic]
    private static bool _raising;

    // Set by Ctrl+R in Insert until the next character, which names the register (only = is one).
    [ThreadStatic]
    private static bool _awaitingRegister;

    // Set while j or k moves the keyboard, so the field it lands on stays in Normal.
    [ThreadStatic]
    private static bool _movingFocus;

    // Where the keyboard is, worked out as it gets there.
    [ThreadStatic]
    private static DependencyObject? _focused;

    [ThreadStatic]
    private static Place _place;

    // On a field: true in Normal, false in Insert.
    [ThreadStatic]
    private static bool _fieldIsNormal;

    // In a grid: true in Insert, which lasts from cell to cell (Tab, Enter, an arrow) until Esc, so a letter or digit on
    // a cell not yet being typed into starts its edit, as it does with the keys off.
    [ThreadStatic]
    private static bool _gridIsInsert;

    // Set while Esc ends a cell's edit: the keyboard moves from the cell's editor to the grid before the cell stops being
    // edited, and that edit mustn't put the grid back in Insert.
    [ThreadStatic]
    private static bool _endingEdit;

    private enum PlaceKind
    {
        Other,
        Field,
        Grid,
        Tree,
        Table,
        Overlay,
    }

    private enum Caret
    {
        Start,
        End,
        All,
    }

    /// <summary>The kind of thing the keyboard is on, and the grid, tree or list it's in.</summary>
    private readonly record struct Place(PlaceKind Kind, UIElement? Container);

    /// <summary>Wires the keys up for every window in the app. Called once, from App's static constructor.</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(OnPreviewTextInput));
        EventManager.RegisterClassHandler(
            typeof(Window), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), handledEventsToo: true);
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VimState.IsEnabled))
            {
                OnEnabledChanged();
            }
        };
    }

    private static void OnEnabledChanged()
    {
        ClearPending();
        VimOverlay.Capturing?.CloseCapture();

        // Worked out afresh from wherever the keyboard is now, in Insert if it's in a field and Normal in a grid.
        _focused = null;
        _place = default;
        _gridIsInsert = false;
        State.Mode = VimMode.Normal;
        if (State.IsEnabled)
        {
            Track(Keyboard.FocusedElement as DependencyObject);
            ShowMode();
        }
    }

    private static void OnIsSearchBoxChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is TextBoxBase box && e.NewValue is true)
        {
            SearchBoxes.RemoveAll(reference => !reference.TryGetTarget(out _));
            SearchBoxes.Add(new WeakReference<TextBoxBase>(box));
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!State.IsEnabled)
        {
            return;
        }

        var focused = e.NewFocus as DependencyObject;
        if (!ReferenceEquals(focused, _focused))
        {
            // A field reached by j or k stays in Normal, so they can go on walking the form. Any other way in — a click,
            // Tab, a hint, the app putting the keyboard there — is to type.
            Arrive(focused, fieldIsNormal: _movingFocus);
        }

        ShowMode();
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!State.IsEnabled || _raising || e.Text.Length != 1 || char.IsControl(e.Text[0]))
        {
            return;
        }

        var key = e.Text[0];
        if (CapturingOverlay(sender) is { } overlay)
        {
            overlay.TakeText(key);
            e.Handled = true;
            return;
        }

        var focused = e.OriginalSource as DependencyObject;
        var place = Track(focused);

        // The character after Ctrl+R names the register. = (the expression register) is the only one there is; any
        // other is dropped, as Vim drops a register it hasn't got.
        if (_awaitingRegister)
        {
            ClearPending();
            e.Handled = true;
            if (key == '=' && IsTyping(place) && sender is Window registerWindow)
            {
                VimOverlay.For(registerWindow)?.ShowExpressionLine(focused, place.Kind == PlaceKind.Grid ? place.Container as SfDataGrid : null);
            }

            ShowMode();
            return;
        }

        if (IsTyping(place))
        {
            return;
        }

        // In Normal no character types, so none can start a cell edit or land in a field.
        e.Handled = true;
        var started = Stopwatch.GetTimestamp();
        var action = Sequence.Feed(key);
        State.Pending = Sequence.Pending;
        if (action != VimAction.None && sender is Window window)
        {
            Perform(action, place, focused, window);
        }

        ShowMode();
        WarnIfSlow(started, e.Text);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!State.IsEnabled || _raising)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = e.KeyboardDevice.Modifiers;
        var focused = e.OriginalSource as DependencyObject;
        var isListOpen = modifiers == ModifierKeys.Control && key is Key.N or Key.P && IsListOpen(focused);
        if (KeyForChord(key, modifiers, isListOpen) is { } stoodFor)
        {
            e.Handled = true;
            PressInstead(e, stoodFor);
            return;
        }

        if (CapturingOverlay(sender) is { } overlay)
        {
            if (overlay.TakeKey(key, IsModifier(key) || TypesText(key, modifiers)))
            {
                e.Handled = true;
            }

            return;
        }

        var place = Track(focused);
        if (place.Kind == PlaceKind.Overlay)
        {
            return;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            if (Escape(place, focused))
            {
                e.Handled = true;
            }

            ShowMode();
            return;
        }

        if (IsTyping(place))
        {
            // Ctrl+R in Insert waits for a register's name, as in Vim: = then takes a sum (OnPreviewTextInput). A key
            // that types nothing (an arrow, Enter, Tab) drops it and does what it does.
            if (key == Key.R && modifiers == ModifierKeys.Control)
            {
                ClearPending();
                _awaitingRegister = true;
                State.Pending = '"';
                e.Handled = true;
            }
            else if (_awaitingRegister && !IsModifier(key) && !TypesText(key, modifiers))
            {
                ClearPending();
            }

            return;
        }

        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            ClearPending();
            if (UndoShortcuts.Undo(focused, redo: true))
            {
                e.Handled = true;
            }

            return;
        }

        // Anything else that types nothing (an arrow, Enter, Tab) ends a half-typed sequence, and does what it does.
        if (Sequence.Pending != '\0' && !IsModifier(key) && !TypesText(key, modifiers))
        {
            ClearPending();
        }
    }

    /// <summary>
    /// Esc, in this order: a sequence half-typed is dropped; a dropdown list open is left to close itself; a cell being
    /// typed into keeps its value and ends its edit (as clicking away does, so a line typed on the add row is added);
    /// a field goes to Normal. Otherwise it's left alone, so a dialog's Cancel and the menu's Esc still work.
    /// </summary>
    private static bool Escape(Place place, DependencyObject? focused)
    {
        if (ClearPending())
        {
            return true;
        }

        switch (place.Kind)
        {
            case PlaceKind.Grid when place.Container is SfDataGrid grid && (_gridIsInsert || IsEditing(grid)):
                if (IsListOpen(focused))
                {
                    return false;
                }

                _gridIsInsert = false;
                if (IsEditing(grid))
                {
                    _endingEdit = true;
                    try
                    {
                        GridEditCommit.Commit(grid, keepCurrentCell: true);
                        if (!grid.IsKeyboardFocusWithin)
                        {
                            grid.Focus();
                        }
                    }
                    finally
                    {
                        _endingEdit = false;
                    }
                }

                return true;

            case PlaceKind.Field when !_fieldIsNormal:
                if (IsListOpen(focused))
                {
                    return false;
                }

                _fieldIsNormal = true;
                return true;

            default:
                return false;
        }
    }

    private static void Perform(VimAction action, Place place, DependencyObject? focused, Window window)
    {
        switch (action)
        {
            case VimAction.NextTab or VimAction.PreviousTab:
                (window as IVimHost)?.StepTab(action == VimAction.NextTab ? 1 : -1);
                return;
            case VimAction.Back:
                (window as IVimHost)?.GoBack();
                return;
            case VimAction.Forward:
                (window as IVimHost)?.GoForward();
                return;
            case VimAction.Undo:
                UndoShortcuts.Undo(focused, redo: false);
                return;
            case VimAction.Search:
                FocusSearchBox(window);
                return;
            case VimAction.Hints:
                VimOverlay.For(window)?.ShowHints();
                return;
            case VimAction.CommandLine:
                VimOverlay.For(window)?.ShowCommandLine(focused, ContextOf(place.Kind));
                return;
            case VimAction.CheatSheet:
                VimOverlay.For(window)?.ShowCheatSheet(ContextOf(place.Kind));
                return;
        }

        switch (place.Kind)
        {
            case PlaceKind.Grid:
                InGrid((SfDataGrid)place.Container!, action, focused);
                break;
            case PlaceKind.Tree:
                InTree((SfTreeView)place.Container!, action, focused);
                break;
            case PlaceKind.Table:
                InTable(place.Container!, action, focused);
                break;
            case PlaceKind.Field:
                InField(focused as TextBox, action, focused);
                break;
            default:
                InOther(action, focused);
                break;
        }
    }

    private static void InGrid(SfDataGrid grid, VimAction action, DependencyObject? focused)
    {
        switch (action)
        {
            case VimAction.Left or VimAction.Down or VimAction.Up or VimAction.Right:
                RaiseKey(focused, ArrowFor(action));
                break;
            case VimAction.First:
                MoveTo(grid, grid.GetFirstDataRowIndex(), CurrentColumn(grid));
                break;
            case VimAction.Last:
                MoveTo(grid, grid.GetLastDataRowIndex(), CurrentColumn(grid));
                break;
            case VimAction.LineStart:
                MoveTo(grid, CurrentRow(grid), grid.GetFirstColumnIndex());
                break;
            case VimAction.LineEnd:
                MoveTo(grid, CurrentRow(grid), grid.GetLastColumnIndex());
                break;
            case VimAction.Insert:
                BeginEdit(grid, Caret.Start);
                break;
            case VimAction.Append:
                BeginEdit(grid, Caret.End);
                break;
            case VimAction.InsertAtStart:
                MoveTo(grid, CurrentRow(grid), grid.GetFirstColumnIndex());
                BeginEdit(grid, Caret.Start);
                break;
            case VimAction.AppendAtEnd:
                MoveTo(grid, CurrentRow(grid), grid.GetLastColumnIndex());
                BeginEdit(grid, Caret.End);
                break;
            case VimAction.Change:
                BeginEdit(grid, Caret.All);
                break;
            case VimAction.Open when grid.AllowEditing && grid.AddNewRowPosition != AddNewRowPosition.None:
                MoveTo(grid, grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex());
                BeginEdit(grid, Caret.Start);
                break;
            case VimAction.OpenAbove when GridRowInserting.CanInsert(grid):
                GridRowInserting.InsertAbove(grid);
                break;
            case VimAction.Delete:
                RaiseKey(focused, Key.Delete);
                break;
            case VimAction.NextControl or VimAction.PreviousControl:
                LeaveContainer(grid, forward: action == VimAction.NextControl);
                break;
        }
    }

    /// <summary>
    /// The Products, Raw Materials and Sub-Assemblies lists. Their own Home only moves the focus border in an Extended list (the tree's
    /// ProcessSingleKeyInteraction), and their arrows do nothing while the row the keyboard was on is hidden (by a
    /// search, or a folder closed over it), so <c>gg</c>, <c>G</c>, and <c>j</c>/<c>k</c> from a hidden row pick their
    /// row themselves, as <c>j</c> and <c>k</c> otherwise do.
    /// </summary>
    private static void InTree(SfTreeView tree, VimAction action, DependencyObject? focused)
    {
        switch (action)
        {
            case VimAction.Down or VimAction.Up when !ShowsRow(tree, tree.CurrentItem):
                if ((action == VimAction.Down ? FirstVisibleNode(tree.Nodes) : LastVisibleNode(tree.Nodes))?.Content is { } start)
                {
                    SelectRow(tree, start);
                }

                break;
            case VimAction.Left or VimAction.Down or VimAction.Up or VimAction.Right:
                RaiseKey(focused, ArrowFor(action));
                break;
            case VimAction.First when FirstVisibleNode(tree.Nodes)?.Content is { } first:
                SelectRow(tree, first);
                break;
            case VimAction.Last when LastVisibleNode(tree.Nodes)?.Content is { } last:
                SelectRow(tree, last);
                break;
            case VimAction.Open:
                RaiseKey(focused, Key.Enter);
                break;
            case VimAction.Delete:
                RaiseKey(focused, Key.Delete);
                break;
            case VimAction.Rename:
                RaiseKey(focused, Key.F2);
                break;
            case VimAction.NextControl or VimAction.PreviousControl:
                LeaveContainer(tree, forward: action == VimAction.NextControl);
                break;
        }
    }

    /// <summary>The Scaling tab's tree grid and the menu: single-selection lists, so End can go as End.</summary>
    private static void InTable(UIElement table, VimAction action, DependencyObject? focused)
    {
        switch (action)
        {
            case VimAction.Left or VimAction.Down or VimAction.Up or VimAction.Right:
                RaiseKey(focused, ArrowFor(action));
                break;
            case VimAction.First:
                RaiseKey(focused, Key.Home);
                break;
            case VimAction.Last:
                RaiseKey(focused, Key.End);
                break;
            case VimAction.Open:
                RaiseKey(focused, Key.Enter);
                break;
            case VimAction.NextControl or VimAction.PreviousControl:
                LeaveContainer(table, forward: action == VimAction.NextControl);
                break;
        }
    }

    private static void InField(TextBox? box, VimAction action, DependencyObject? focused)
    {
        switch (action)
        {
            case VimAction.Down or VimAction.NextControl:
                MoveFocusFrom(focused, FocusNavigationDirection.Next);
                return;
            case VimAction.Up or VimAction.PreviousControl:
                MoveFocusFrom(focused, FocusNavigationDirection.Previous);
                return;
        }

        if (box is null)
        {
            return;
        }

        switch (action)
        {
            case VimAction.Left:
                box.Select(Math.Max(0, box.CaretIndex - 1), 0);
                break;
            case VimAction.Right:
                box.Select(Math.Min(box.Text.Length, box.CaretIndex + 1), 0);
                break;
            case VimAction.LineStart:
                box.Select(0, 0);
                break;
            case VimAction.LineEnd:
                box.Select(box.Text.Length, 0);
                break;
            case VimAction.Insert:
                _fieldIsNormal = false;
                break;
            case VimAction.Append:
                box.Select(Math.Min(box.Text.Length, box.CaretIndex + 1), 0);
                _fieldIsNormal = false;
                break;
            case VimAction.InsertAtStart:
                box.Select(0, 0);
                _fieldIsNormal = false;
                break;
            case VimAction.AppendAtEnd:
                box.Select(box.Text.Length, 0);
                _fieldIsNormal = false;
                break;
            case VimAction.Change:
                box.SelectAll();
                _fieldIsNormal = false;
                break;
        }
    }

    /// <summary>A button, a tab, a check box, or nothing at all: <c>j</c>/<c>k</c> walk the controls as Tab does.</summary>
    private static void InOther(VimAction action, DependencyObject? focused)
    {
        switch (action)
        {
            case VimAction.Down or VimAction.NextControl:
                MoveFocusFrom(focused, FocusNavigationDirection.Next);
                break;
            case VimAction.Up or VimAction.PreviousControl:
                MoveFocusFrom(focused, FocusNavigationDirection.Previous);
                break;
            case VimAction.Left or VimAction.Right:
                RaiseKey(focused, ArrowFor(action));
                break;
        }
    }

    private static Key ArrowFor(VimAction action) => action switch
    {
        VimAction.Left => Key.Left,
        VimAction.Up => Key.Up,
        VimAction.Right => Key.Right,
        _ => Key.Down,
    };

    /// <summary>
    /// The key a Vim chord stands for, or null when <paramref name="key"/> with <paramref name="modifiers"/> isn't one:
    /// Ctrl+[ is Esc anywhere, and Ctrl+N and Ctrl+P are Down and Up while a dropdown or suggestion list is open.
    /// </summary>
    public static Key? KeyForChord(Key key, ModifierKeys modifiers, bool isListOpen) =>
        modifiers != ModifierKeys.Control ? null : key switch
        {
            Key.OemOpenBrackets => Key.Escape,
            Key.N when isListOpen => Key.Down,
            Key.P when isListOpen => Key.Up,
            _ => null,
        };

    /// <summary>
    /// Presses <paramref name="key"/> in place of <paramref name="chord"/>, as if it were typed alone. It goes through
    /// the input manager, as a real press does, and not just to the element with the keyboard: a dialog's Cancel button
    /// takes Esc as an access key, which only the input manager's own processing hands it. Ctrl is still held, and WPF
    /// reads it live from the thread's key state (GetKeyState), so it's lifted from that state while the key goes
    /// through: a list's own Down would otherwise be Ctrl+Down, which jumps to its last row.
    /// </summary>
    private static void PressInstead(KeyEventArgs chord, Key key)
    {
        var held = new byte[256];
        if (chord.InputSource is not { } source || !GetKeyboardState(held))
        {
            return;
        }

        var lifted = (byte[])held.Clone();
        foreach (var control in (ReadOnlySpan<int>)[VkControl, VkLeftControl, VkRightControl])
        {
            lifted[control] &= 0x7F;
        }

        SetKeyboardState(lifted);
        try
        {
            InputManager.Current.ProcessInput(new KeyEventArgs(chord.KeyboardDevice, source, chord.Timestamp, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        finally
        {
            SetKeyboardState(held);
        }
    }

    /// <summary>
    /// Types the answer to a Ctrl+R = sum into <paramref name="target"/>, the box the keyboard is in, at its caret and
    /// over what's selected, as typing it would. A grid in Insert between cells (after Tab or Enter) has no box yet, so
    /// its current cell's edit is begun first, as a digit typed there would. Null when it's typed, otherwise why not.
    /// </summary>
    internal static string? TypeAnswer(DependencyObject? target, SfDataGrid? grid, decimal answer)
    {
        if (target is TextBoxBase box && (grid is null || IsEditing(grid)))
        {
            TypeInto(box, answer);
            return null;
        }

        if (grid?.SelectionController?.CurrentCellManager is not { HasCurrentCell: true } cells)
        {
            return "Ctrl+R = types its answer into a cell or a box being typed into.";
        }

        // GridCellEditing's refusals still hold: a cell that can't be typed into begins no edit.
        if (!IsEditing(grid) && !cells.BeginEdit())
        {
            return "This cell can't be typed into.";
        }

        // The cell's editor takes the keyboard once it has loaded, which is after this.
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Keyboard.FocusedElement is TextBoxBase editor && VisualTreeWalk.IsWithin(grid, editor))
            {
                TypeInto(editor, answer);
            }
        });
        return null;
    }

    /// <summary>
    /// Types <paramref name="answer"/> into <paramref name="box"/> a character at a time, through the input manager, so
    /// a number box takes it as it takes keys: its own mask, limits and decimals, and an ordinary edit for undo. In a
    /// number box it's first rounded to the decimals the box takes, as the mask would otherwise cut it short.
    /// </summary>
    private static void TypeInto(TextBoxBase box, decimal answer)
    {
        var text = VimExpression.Format(answer, DecimalsTaken(box), DecimalSeparator(box));
        _raising = true;
        try
        {
            foreach (var character in text)
            {
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, character.ToString()));
            }
        }
        finally
        {
            _raising = false;
        }
    }

    /// <summary>
    /// How many decimals <paramref name="box"/> takes when typed into: what caps the typing (MaximumNumberDecimalDigits,
    /// which NumericEditRenderer sets for a column that takes more than it shows), else what it shows. Null for a box of
    /// text, or a number box that sets neither, which keeps every decimal of the answer.
    /// </summary>
    private static int? DecimalsTaken(TextBoxBase box) => box switch
    {
        DoubleTextBox number when number.MaximumNumberDecimalDigits >= 0 => number.MaximumNumberDecimalDigits,
        DoubleTextBox number when number.NumberDecimalDigits >= 0 => number.NumberDecimalDigits,
        IntegerTextBox => 0,
        CurrencyTextBox money when money.MaximumCurrencyDecimalDigits >= 0 => money.MaximumCurrencyDecimalDigits,
        CurrencyTextBox money when money.CurrencyDecimalDigits >= 0 => money.CurrencyDecimalDigits,
        PercentTextBox percent when percent.PercentDecimalDigits >= 0 => percent.PercentDecimalDigits,
        _ => null,
    };

    /// <summary>The decimal point <paramref name="box"/> types: a number box's culture's, a box of text's <c>.</c>.</summary>
    private static string DecimalSeparator(TextBoxBase box) => box switch
    {
        DoubleTextBox number => (number.NumberFormat ?? number.Culture?.NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat).NumberDecimalSeparator,
        EditorBase => CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
        _ => ".",
    };

    /// <summary>
    /// Raises <paramref name="key"/> on <paramref name="target"/> as a real press reaches it: PreviewKeyDown, then KeyDown
    /// unless the preview was handled, as WPF's KeyboardDevice does (PostProcessInput).
    /// </summary>
    internal static void RaiseKey(DependencyObject? target, Key key)
    {
        if (target is not IInputElement element || PresentationSource.FromDependencyObject(target) is not { } source)
        {
            return;
        }

        _raising = true;
        try
        {
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            element.RaiseEvent(preview);
            if (!preview.Handled)
            {
                element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
            }
        }
        finally
        {
            _raising = false;
        }
    }

    private static int CurrentRow(SfDataGrid grid)
    {
        GridKeyboardFocus.PlaceCurrentCell(grid);
        return grid.SelectionController?.CurrentCellManager is { HasCurrentCell: true } cells
            ? cells.CurrentRowColumnIndex.RowIndex
            : -1;
    }

    private static int CurrentColumn(SfDataGrid grid) =>
        grid.SelectionController?.CurrentCellManager is { HasCurrentCell: true } cells
            ? Math.Max(cells.CurrentRowColumnIndex.ColumnIndex, grid.GetFirstColumnIndex())
            : grid.GetFirstColumnIndex();

    /// <summary>Puts the grid's current cell on <paramref name="row"/> and <paramref name="column"/>, and in view.</summary>
    private static void MoveTo(SfDataGrid grid, int row, int column)
    {
        if (row < 0 || column < 0)
        {
            return;
        }

        var cell = new RowColumnIndex(row, column);
        grid.MoveCurrentCell(cell);
        grid.ScrollInView(cell);
    }

    /// <summary>A hint on a grid's row: the keyboard on the grid, and its current cell on the row (picking it, in a list).</summary>
    internal static void OpenGridRow(SfDataGrid grid, int row)
    {
        grid.Focus();
        MoveTo(grid, row, CurrentColumn(grid));
    }

    /// <summary>Starts the current cell's edit, with the caret where <paramref name="caret"/> says once the editor is up.</summary>
    private static void BeginEdit(SfDataGrid grid, Caret caret)
    {
        GridKeyboardFocus.PlaceCurrentCell(grid);
        if (grid.SelectionController?.CurrentCellManager is not { HasCurrentCell: true } cells || !cells.BeginEdit())
        {
            return;
        }

        // The editor takes the keyboard as it loads, which is after this.
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (Keyboard.FocusedElement is TextBox box && VisualTreeWalk.IsWithin(grid, box))
            {
                switch (caret)
                {
                    case Caret.Start:
                        box.Select(0, 0);
                        break;
                    case Caret.End:
                        box.Select(box.Text.Length, 0);
                        break;
                    default:
                        box.SelectAll();
                        break;
                }
            }
        });
    }

    private static TreeViewNode? FirstVisibleNode(TreeViewNodeCollection nodes) => nodes.Count > 0 ? nodes[0] : null;

    /// <summary>Whether <paramref name="item"/>'s row is one the list shows: in it, and in no closed folder.</summary>
    private static bool ShowsRow(SfTreeView tree, object? item) =>
        item is not null && VisibleNodes(tree.Nodes).Any(node => ReferenceEquals(node.Content, item));

    private static IEnumerable<TreeViewNode> VisibleNodes(TreeViewNodeCollection nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.IsExpanded)
            {
                foreach (var child in VisibleNodes(node.ChildNodes))
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>The last row a list shows: its last top row, and down through the last row of each open folder.</summary>
    private static TreeViewNode? LastVisibleNode(TreeViewNodeCollection nodes)
    {
        var node = nodes.Count > 0 ? nodes[^1] : null;
        while (node is { IsExpanded: true, ChildNodes.Count: > 0 })
        {
            node = node.ChildNodes[^1];
        }

        return node;
    }

    /// <summary>A hint on a list's row: the keyboard on the list, and that row picked alone.</summary>
    internal static void OpenTreeRow(SfTreeView tree, object item)
    {
        tree.Focus();
        SelectRow(tree, item);
    }

    /// <summary>
    /// A hint on a picker: the keyboard in the box it's typed in, when it's typed in. Not through the picker itself,
    /// which hands it on to the box only while it's focusable: a Shift+Tab out of the box makes it unfocusable for good
    /// (ComboBoxAdv's Part_IsEdit_PreviewKeyDown sets Focusable to false, so Shift+Tab doesn't land back on the picker,
    /// and nothing sets it back), and its Focus() then does nothing.
    /// </summary>
    public static void FocusPicker(ComboBoxAdv picker)
    {
        if (picker.IsEditable && picker.Template?.FindName("PART_Editable", picker) is TextBox { IsVisible: true } box)
        {
            box.Focus();
        }
        else
        {
            picker.Focus();
        }
    }

    /// <summary>
    /// Picks <paramref name="item"/>'s row alone, as a click does, and scrolls to it. Through SelectedItems, the
    /// collection the screens bind: in Extended mode a SelectedItem set from outside is added in front of the rows
    /// already picked, not put in their place (the tree's ProcessSelectionPropertiesChanged).
    /// </summary>
    private static void SelectRow(SfTreeView tree, object item)
    {
        if (tree.SelectedItems is { } selected && (selected.Count != 1 || !ReferenceEquals(selected[0], item)))
        {
            selected.Clear();
            selected.Add(item);
        }

        tree.BringIntoView(item, disableAnimation: true, canExpand: false, ScrollToPosition.MakeVisible);
    }

    private static void MoveFocusFrom(DependencyObject? from, FocusNavigationDirection direction)
    {
        if (from is not UIElement element)
        {
            return;
        }

        _movingFocus = true;
        try
        {
            element.MoveFocus(new TraversalRequest(direction));
        }
        finally
        {
            _movingFocus = false;
        }
    }

    /// <summary>
    /// Takes the keyboard out of a grid or list, to the control after it (or before it), as Tab would from its edge.
    /// Moving on from the container itself can land inside it first, so it keeps going until it's out.
    /// </summary>
    private static void LeaveContainer(UIElement container, bool forward)
    {
        var direction = forward ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous;
        _movingFocus = true;
        try
        {
            var from = container;
            for (var step = 0; step < 64 && from.MoveFocus(new TraversalRequest(direction)); step++)
            {
                if (Keyboard.FocusedElement is not UIElement now || !VisualTreeWalk.IsWithin(container, now))
                {
                    return;
                }

                from = now;
            }
        }
        finally
        {
            _movingFocus = false;
        }
    }

    /// <summary>
    /// Puts the keyboard back on <paramref name="element"/> in Normal, as it was before the command line took it.
    /// </summary>
    internal static void FocusInNormal(IInputElement element)
    {
        _movingFocus = true;
        try
        {
            element.Focus();
        }
        finally
        {
            _movingFocus = false;
        }
    }

    /// <summary><c>/</c>: the keyboard in the search of the list on show, its text picked so what's typed replaces it.</summary>
    private static void FocusSearchBox(Window window)
    {
        foreach (var reference in SearchBoxes)
        {
            if (reference.TryGetTarget(out var box) && box.IsVisible && ReferenceEquals(Window.GetWindow(box), window))
            {
                box.Focus();
                box.SelectAll();
                return;
            }
        }
    }

    private static Place Track(DependencyObject? focused)
    {
        if (!ReferenceEquals(focused, _focused))
        {
            Arrive(focused, fieldIsNormal: false);
        }

        return _place;
    }

    /// <summary>
    /// The keyboard has moved to <paramref name="focused"/>: where it is now, and the mode it's in there. A field starts in
    /// Normal or Insert as <paramref name="fieldIsNormal"/> says. A grid keeps its mode while the keyboard moves within it
    /// (into a cell's editor and back); come to from somewhere else, it takes the mode the keys were in, so Tab from a
    /// field being typed into goes on typing in the grid.
    /// </summary>
    private static void Arrive(DependencyObject? focused, bool fieldIsNormal)
    {
        var wasInsert = State.Mode == VimMode.Insert;
        var previous = _place;
        _focused = focused;
        _place = PlaceOf(focused);
        _fieldIsNormal = fieldIsNormal;
        if (_place.Kind == PlaceKind.Grid && !ReferenceEquals(_place.Container, previous.Container))
        {
            _gridIsInsert = wasInsert;
        }
    }

    private static Place PlaceOf(DependencyObject? element)
    {
        var isText = element is TextBoxBase;
        foreach (var ancestor in VisualTreeWalk.SelfAndAncestors(element))
        {
            switch (ancestor)
            {
                case VimOverlay:
                    return new Place(PlaceKind.Overlay, null);

                // A column filter's pop-up hangs from the funnel in the grid's heading, but isn't the grid: its search box is a
                // field. The walk steps from the pop-up (the filter control's PART_FilterPopup) to the funnel, past the control.
                case Popup { TemplatedParent: GridFilterControl }:
                    return new Place(isText ? PlaceKind.Field : PlaceKind.Other, null);
                case SfDataGrid grid:
                    return new Place(PlaceKind.Grid, grid);
                case SfTreeView tree:
                    return new Place(PlaceKind.Tree, tree);
                case SfTreeGrid treeGrid:
                    return new Place(PlaceKind.Table, treeGrid);
                case ListBox list when !isText:
                    return new Place(PlaceKind.Table, list);
                case Window:
                    return new Place(isText ? PlaceKind.Field : PlaceKind.Other, null);
            }
        }

        return new Place(isText ? PlaceKind.Field : PlaceKind.Other, null);
    }

    private static VimContexts ContextOf(PlaceKind kind) => kind switch
    {
        PlaceKind.Field => VimContexts.Field,
        PlaceKind.Grid or PlaceKind.Table => VimContexts.Table,
        PlaceKind.Tree => VimContexts.Tree,
        _ => VimContexts.Other,
    };

    private static bool IsTyping(Place place) => place.Kind switch
    {
        PlaceKind.Field => !_fieldIsNormal,
        PlaceKind.Grid => IsGridInsert((SfDataGrid)place.Container!),
        PlaceKind.Overlay => true,
        _ => false,
    };

    /// <summary>
    /// Whether the grid is in Insert: a cell is being typed into, however that began (<c>i</c>, a click, a letter), or one
    /// was and no Esc has come since. A cell seen being typed into puts the grid in Insert until then.
    /// </summary>
    private static bool IsGridInsert(SfDataGrid grid)
    {
        if (!_endingEdit && IsEditing(grid))
        {
            _gridIsInsert = true;
        }

        return _gridIsInsert;
    }

    private static bool IsEditing(SfDataGrid grid) =>
        grid.SelectionController?.CurrentCellManager is { HasCurrentCell: true, CurrentCell.IsEditing: true };

    /// <summary>Whether a picker's or cell's dropdown list is open, which Esc closes before anything else.</summary>
    private static bool IsListOpen(DependencyObject? focused)
    {
        foreach (var ancestor in VisualTreeWalk.SelfAndAncestors(focused))
        {
            switch (ancestor)
            {
                case ComboBoxAdv { IsDropDownOpen: true }:
                case ComboBox { IsDropDownOpen: true }:
                case SfMultiColumnDropDownControl { IsDropDownOpen: true }:
                case SfTextBoxExt { IsSuggestionOpen: true }:
                    return true;
                case SfDataGrid or Window:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The overlay taking the keys (hints or the cheat sheet up), when it's <paramref name="window"/>'s. One left up in a
    /// window that has gone (closed while its hints showed) is put away, so it can't take another window's keys.
    /// </summary>
    private static VimOverlay? CapturingOverlay(object window)
    {
        if (VimOverlay.Capturing is not { } overlay)
        {
            return null;
        }

        if (overlay.IsFor(window))
        {
            return overlay;
        }

        overlay.CloseCapture();
        return null;
    }

    /// <summary>Shows the mode the keys are in now in the title bar.</summary>
    internal static void ShowMode() =>
        State.Mode = VimOverlay.Capturing is { IsShowingHints: true } ? VimMode.Hints
            : _place.Kind == PlaceKind.Overlay || VimOverlay.Capturing is { IsShowingExpression: true } ? VimMode.Command
            : IsTyping(_place) ? VimMode.Insert
            : VimMode.Normal;

    private static bool ClearPending()
    {
        var had = Sequence.Clear() | _awaitingRegister;
        _awaitingRegister = false;
        State.Pending = '\0';
        return had;
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock;

    /// <summary>Whether <paramref name="key"/> types a character, which comes on as text input. Ctrl or Alt alone types
    /// nothing; both together is AltGr, which can.</summary>
    private static bool TypesText(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Control) == modifiers.HasFlag(ModifierKeys.Alt)
        && key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.Divide)
            or (>= Key.Oem1 and <= Key.Oem102) or Key.Space or Key.ImeProcessed or Key.DeadCharProcessed;

    [Conditional("DEBUG")]
    private static void WarnIfSlow(long started, string key)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed.TotalMilliseconds > 4)
        {
            Log.Warning("The Vim key {Key} took {Milliseconds:0.0} ms", key, elapsed.TotalMilliseconds);
        }
    }

    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] keyState);
}
