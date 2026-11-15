using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The sign-in panel the locked window shows (AppLock). With a password set it asks for it; with none yet (a new
/// database, or one cleared by scripts/Reset-Password.ps1) it asks for a new one, twice, and that sets it. Either way the
/// window then unlocks, with the auto-lock's minutes read from the database. What's typed is cleared after every try,
/// and a database that can't be reached says so in the panel, where a Try Again reads it again.
/// </summary>
public sealed class SignInViewModel : ViewModelBase
{
    private readonly IAccessSettingsRepository _repository;
    private readonly AppLock _appLock;

    private bool _isLoaded;
    /// <summary>Whether it's known yet if a password is set: until then the panel shows neither form.</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => this.RaiseAndSetIfChanged(ref _isLoaded, value);
    }

    private bool _isSettingUp;
    /// <summary>True when there's no password yet, so the panel asks for a new one.</summary>
    public bool IsSettingUp
    {
        get => _isSettingUp;
        private set => this.RaiseAndSetIfChanged(ref _isSettingUp, value);
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value ?? "");
    }

    private string _confirmation = "";
    /// <summary>The new password typed a second time, when setting one up.</summary>
    public string Confirmation
    {
        get => _confirmation;
        set => this.RaiseAndSetIfChanged(ref _confirmation, value ?? "");
    }

    /// <summary>Reads whether a password is set, clearing what was typed and said last time.</summary>
    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SignInCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SetPasswordCommand { get; }

    public SignInViewModel(IAccessSettingsRepository repository, AppLock appLock, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _appLock = appLock;

        LoadCommand = ReactiveCommand.CreateFromTask(() =>
        {
            Password = "";
            Confirmation = "";
            ErrorMessage = null;
            return LoadSafelyAsync(async () =>
            {
                var settings = await _repository.GetAsync(CancellationToken.None);
                IsSettingUp = !settings.HasPassword;
                IsLoaded = true;
            });
        });

        SignInCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(SignInAsync));
        SetPasswordCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(SetPasswordAsync));
    }

    private async Task SignInAsync()
    {
        var typed = Password;
        Password = "";
        if (_appLock.WaitLeft is { } wait && wait > TimeSpan.Zero)
        {
            ErrorMessage = WaitText(wait);
            return;
        }

        if (typed.Length == 0)
        {
            ErrorMessage = "Type the password.";
            return;
        }

        if (await _repository.VerifyAsync(typed, CancellationToken.None))
        {
            await UnlockAsync();
            return;
        }

        // A password cleared since the panel opened (Reset-Password.ps1) can't be given: ask for a new one instead.
        if (!(await _repository.GetAsync(CancellationToken.None)).HasPassword)
        {
            IsSettingUp = true;
            ErrorMessage = "There's no password any more: set a new one.";
            return;
        }

        _appLock.NoteWrongTry();
        ErrorMessage = _appLock.WaitLeft is { } waitNow && waitNow > TimeSpan.Zero
            ? $"That isn't the password. {WaitText(waitNow)}"
            : "That isn't the password.";
    }

    private async Task SetPasswordAsync()
    {
        var (typed, again) = (Password, Confirmation);
        Password = "";
        Confirmation = "";
        PasswordRules.EnsureValid(typed);
        PasswordRules.EnsureMatches(typed, again);

        if ((await _repository.GetAsync(CancellationToken.None)).HasPassword)
        {
            IsSettingUp = false;
            ErrorMessage = "A password has been set already: sign in with it.";
            return;
        }

        await _repository.CreatePasswordAsync(typed, CancellationToken.None);
        await UnlockAsync();
    }

    private async Task UnlockAsync()
    {
        var settings = await _repository.GetAsync(CancellationToken.None);
        _appLock.SetAutoLockMinutes(settings.AutoLockMinutes);
        IsSettingUp = false;
        _appLock.Unlock();
    }

    private static string WaitText(TimeSpan wait) =>
        $"Too many wrong tries: wait {Math.Ceiling(wait.TotalSeconds):0} seconds, then try again.";
}
