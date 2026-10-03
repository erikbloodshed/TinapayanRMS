using System.ComponentModel;

namespace TinapayanRMS.UI.Vim;

/// <summary>What the Vim keys are doing: in Normal they move and run commands, in Insert they type.</summary>
public enum VimMode
{
    Normal,
    Insert,
    Hints,
    Command,
}

/// <summary>
/// Whether the Vim keys are on, the mode they're in and the first key of a sequence typed so far, for the title bar's
/// indicator. It only raises a change when a value actually changes, so a key that leaves them as they were costs the
/// indicator nothing.
/// </summary>
public sealed class VimState : INotifyPropertyChanged
{
    private bool _isEnabled;
    private VimMode _mode;
    private char _pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether the Vim keys are on (Settings' Keyboard tab). Off, every key does what it always did.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                _pending = '\0';
                Raise(nameof(IsEnabled));
                Raise(nameof(Indicator));
            }
        }
    }

    public VimMode Mode
    {
        get => _mode;
        internal set
        {
            if (_mode != value)
            {
                _mode = value;
                Raise(nameof(Mode));
                Raise(nameof(Indicator));
            }
        }
    }

    /// <summary>The first key of a sequence typed so far, or '\0'.</summary>
    public char Pending
    {
        get => _pending;
        internal set
        {
            if (_pending != value)
            {
                _pending = value;
                Raise(nameof(Pending));
                Raise(nameof(Indicator));
            }
        }
    }

    /// <summary>The mode as the title bar shows it, with the key typed so far after it: "NORMAL", "INSERT", "NORMAL g".</summary>
    public string Indicator
    {
        get
        {
            var mode = Mode switch
            {
                VimMode.Insert => "INSERT",
                VimMode.Hints => "HINTS",
                VimMode.Command => "COMMAND",
                _ => "NORMAL",
            };
            return Pending == '\0' ? mode : $"{mode} {Pending}";
        }
    }

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
