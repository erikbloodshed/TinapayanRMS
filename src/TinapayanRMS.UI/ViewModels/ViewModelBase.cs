using System.Reactive.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Shared behavior for every screen's ViewModel: a friendly ErrorMessage surfaced in the UI
/// instead of letting a validation/business-rule exception fail silently or crash the app,
/// an IsBusy flag for a simple loading indicator during async saves, and a Delete-
/// confirmation Interaction the View wires up to a native MessageBox. Introduced in Phase 13
/// once every screen needed the exact same three things — not before, since one screen
/// needing them wouldn't have justified a shared base class over ReactiveObject directly.
/// </summary>
public abstract class ViewModelBase : ReactiveObject
{
    private readonly ILogger _logger;

    protected ViewModelBase(ILogger logger)
    {
        _logger = logger.ForContext(GetType());

        _hasError = this.WhenAnyValue(x => x.ErrorMessage)
            .Select(message => !string.IsNullOrEmpty(message))
            .ToProperty(this, x => x.HasError);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _hasError;
    public bool HasError => _hasError.Value;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    /// <summary>Input: a confirmation message to show the user. Output: true if they confirmed. The View registers a handler that shows a native MessageBox.</summary>
    public Interaction<string, bool> ConfirmDelete { get; } = new();

    /// <summary>
    /// Runs action with IsBusy toggled around it, clearing any previous ErrorMessage first.
    /// A business-rule violation (InvalidOperationException, thrown by every ApplicationCore
    /// Rules class), a duplicate-name save (DbUpdateException, a unique-index violation), or
    /// a delete blocked by a Restrict foreign key (SqlException, raised by ExecuteDeleteAsync
    /// outside the change tracker — see PLAN.md) are caught and shown as ErrorMessage instead
    /// of crashing or failing silently. Any other exception is a real bug, so it still
    /// propagates rather than being hidden behind a generic message.
    /// </summary>
    protected async Task RunSafelyAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            _logger.Warning(ex, "Rejected by a business rule");
        }
        catch (DbUpdateException ex)
        {
            ErrorMessage = "That name is already in use.";
            _logger.Warning(ex, "Rejected by a database constraint");
        }
        catch (SqlException ex)
        {
            ErrorMessage = "This item is currently in use elsewhere and can't be deleted.";
            _logger.Warning(ex, "Rejected by a foreign key constraint");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks the user to confirm via ConfirmDelete, then runs action (through RunSafelyAsync) only if they agree.</summary>
    protected async Task RunWithConfirmationAsync(string confirmationMessage, Func<Task> action)
    {
        var confirmed = await ConfirmDelete.Handle(confirmationMessage);
        if (!confirmed)
        {
            return;
        }

        await RunSafelyAsync(action);
    }
}
