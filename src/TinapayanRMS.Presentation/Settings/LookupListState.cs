using System.Collections.ObjectModel;
using DynamicData;
using FluentValidation;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// A Settings tab that keeps one of the app's short lists (units of measure, raw material and product categories, roles):
/// the list, and a form beside it. Picking an item loads it for editing; New, or finishing a Save or Delete, clears the
/// form, so the next Save always adds one rather than changing the last one touched. Each tab says what its form holds
/// beyond the name (<see cref="LoadIntoForm"/>, <see cref="FormItem"/>, <see cref="ClearFields"/>), how its list is read
/// and written, and what Delete asks.
/// </summary>
/// <typeparam name="TItem">The kind of item the list holds.</typeparam>
public abstract class LookupListState<TItem> : ScreenState, IDisposable
    where TItem : class
{
    private readonly IValidator<TItem> _validator;
    private readonly Func<TItem, int> _idOf;
    private readonly string _noun;
    private readonly SourceCache<TItem, int> _cache;
    private readonly ReadOnlyObservableCollection<TItem> _items;

    /// <param name="noun">What an item is called in the form's title: "Unit", "Category", "Role".</param>
    /// <param name="idOf">An item's id.</param>
    /// <param name="order">The order the list shows its items in.</param>
    /// <param name="validator">What Save checks the form's item against.</param>
    /// <param name="logger">Where a failure is logged.</param>
    protected LookupListState(string noun, Func<TItem, int> idOf, IComparer<TItem> order, IValidator<TItem> validator, ILogger logger)
        : base(logger)
    {
        _noun = noun;
        _idOf = idOf;
        _validator = validator;
        _cache = new SourceCache<TItem, int>(idOf);

        _cache.Connect()
            .SortAndBind(out _items, order)
            .Subscribe();

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var items = await ReadAllAsync();
            var selectedId = SelectedItem is { } selected ? _idOf(selected) : (int?)null;

            ApplyRefresh(() =>
            {
                _cache.MergeFrom(items);
                SelectedItem = Items.FirstOrDefault(i => _idOf(i) == selectedId);
            });
        }));

        NewCommand = ScreenCommand.Of(ClearForm);

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var item = FormItem();

            var validation = _validator.Validate(item);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            await SaveAsync(item);
        }));

        DeleteCommand = new ScreenCommand(
            () => RunWithConfirmationAsync(DeleteQuestion(SelectedItem!), async () =>
            {
                await DeleteAsync(_idOf(SelectedItem!));
                await FinishSaveAsync();
            }),
            () => SelectedItem is not null);
    }

    /// <summary>The list, in its order.</summary>
    public ReadOnlyObservableCollection<TItem> Items => _items;

    private TItem? _selectedItem;

    /// <summary>The item picked in the list, which the form shows for editing; null while the form is a new one's.</summary>
    public TItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            // Not while LoadCommand merges, which only re-points the selection at its refreshed copy.
            if (RaiseAndSetIfChanged(ref _selectedItem, value) && value is { } item && !IsRefreshing)
            {
                EditingId = _idOf(item);
                LoadIntoForm(item);
            }
        }
    }

    /// <summary>The id of the item the form edits; 0 for a new one.</summary>
    protected int EditingId { get; private set; }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    /// <summary>"New Unit", or "Edit Unit" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => SelectedItem is null ? $"New {_noun}" : $"Edit {_noun}";

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand NewCommand { get; }
    public ScreenCommand SaveCommand { get; }
    public ScreenCommand DeleteCommand { get; }

    /// <summary>Every item, as stored.</summary>
    protected abstract Task<IReadOnlyList<TItem>> ReadAllAsync();

    protected abstract Task AddAsync(TItem item);

    protected abstract Task UpdateAsync(TItem item);

    protected abstract Task DeleteAsync(int id);

    /// <summary>What Delete asks before deleting <paramref name="item"/>.</summary>
    protected abstract string DeleteQuestion(TItem item);

    /// <summary>Puts <paramref name="item"/>'s name and the rest of what the form shows into the form.</summary>
    protected abstract void LoadIntoForm(TItem item);

    /// <summary>The item the form holds, under <see cref="EditingId"/>, for Save to check and write.</summary>
    protected abstract TItem FormItem();

    /// <summary>Puts what the form shows beyond the name back to a new item's.</summary>
    protected abstract void ClearFields();

    /// <summary>
    /// Writes <paramref name="item"/>, checked: adds it when the form is a new one's, or writes it over the one picked,
    /// then <see cref="FinishSaveAsync"/>.
    /// </summary>
    protected virtual async Task SaveAsync(TItem item)
    {
        if (EditingId == 0)
        {
            await AddAsync(item);
        }
        else
        {
            await UpdateAsync(item);
        }

        await FinishSaveAsync();
    }

    /// <summary>After a Save or Delete: the form cleared for a new item, and the list read again.</summary>
    protected async Task FinishSaveAsync()
    {
        ClearForm();
        await LoadCommand.ExecuteAsync();
    }

    private void ClearForm()
    {
        SelectedItem = null;
        EditingId = 0;
        EditingName = string.Empty;
        ClearFields();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
