using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Security tab: the app's password, changed by giving the current one and the new one twice (or, with none
/// set, as a restore of an older backup can leave it, set by the new one alone), and the idle minutes before the window
/// locks itself, which take effect at once (AppLock). Both are kept in the database (AccessSettings).
/// </summary>
public sealed class SecuritySettingsState : ScreenState
{
    private readonly IAccessSettingsRepository _repository;
    private readonly AppLock _appLock;

    private bool _hasPassword = true;
    /// <summary>Whether a password is set; without one, Current Password isn't asked for.</summary>
    public bool HasPassword
    {
        get => _hasPassword;
        private set => RaiseAndSetIfChanged(ref _hasPassword, value);
    }

    private string _currentPassword = "";
    public string CurrentPassword
    {
        get => _currentPassword;
        set => RaiseAndSetIfChanged(ref _currentPassword, value ?? "");
    }

    private string _newPassword = "";
    public string NewPassword
    {
        get => _newPassword;
        set => RaiseAndSetIfChanged(ref _newPassword, value ?? "");
    }

    private string _confirmation = "";
    /// <summary>The new password typed a second time.</summary>
    public string Confirmation
    {
        get => _confirmation;
        set => RaiseAndSetIfChanged(ref _confirmation, value ?? "");
    }

    private int _editingAutoLockMinutes = Domain.Entities.AccessSettings.DefaultAutoLockMinutes;
    /// <summary>Idle minutes before the window locks itself, 0 to PasswordRules.MaxAutoLockMinutes; 0 for never.</summary>
    public int EditingAutoLockMinutes
    {
        get => _editingAutoLockMinutes;
        set => RaiseAndSetIfChanged(ref _editingAutoLockMinutes, value);
    }

    public ScreenCommand LoadCommand { get; }

    public ScreenCommand ChangePasswordCommand { get; }

    public ScreenCommand SaveAutoLockCommand { get; }

    public SecuritySettingsState(IAccessSettingsRepository repository, AppLock appLock, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _appLock = appLock;

        LoadCommand = new ScreenCommand(() =>
        {
            ClearPasswords();
            return LoadSafelyAsync(async () =>
            {
                var settings = await _repository.GetAsync(CancellationToken.None);
                HasPassword = settings.HasPassword;
                EditingAutoLockMinutes = settings.AutoLockMinutes;
            });
        });

        ChangePasswordCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var (current, typed, again) = (CurrentPassword, NewPassword, Confirmation);
            ClearPasswords();
            PasswordRules.EnsureValid(typed);
            PasswordRules.EnsureMatches(typed, again);
            if (HasPassword)
            {
                await _repository.ChangePasswordAsync(current, typed, CancellationToken.None);
                SuccessMessage = "The password was changed: use the new one from now on.";
            }
            else
            {
                await _repository.CreatePasswordAsync(typed, CancellationToken.None);
                HasPassword = true;
                SuccessMessage = "The password was set: the app asks for it as it opens and when it's locked.";
            }
        }));

        SaveAutoLockCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var minutes = EditingAutoLockMinutes;
            await _repository.SetAutoLockMinutesAsync(minutes, CancellationToken.None);
            _appLock.SetAutoLockMinutes(minutes);
            SuccessMessage = minutes == 0
                ? "Auto-lock saved: the window locks only when you click Lock."
                : $"Auto-lock saved: the window locks after {minutes} {(minutes == 1 ? "minute" : "minutes")} without a key, click or scroll.";
        }));
    }

    private void ClearPasswords()
    {
        CurrentPassword = "";
        NewPassword = "";
        Confirmation = "";
    }
}
