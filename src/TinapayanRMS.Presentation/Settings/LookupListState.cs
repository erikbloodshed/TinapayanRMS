using System.Collections.ObjectModel;
using DynamicData;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Settings;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// A Settings tab that keeps one of the app's short lists (units of measure, raw material and product categories, roles):
/// the list, and a form beside it. Picking an item loads it for editing; New, or finishing a Save or Delete, clears the
/// form, so the next Save always adds one rather than changing the last one touched. Each tab says what its form holds
/// beyond the name (<see cref="LoadIntoForm"/>, <see cref="FormItem"/>, <see cref="ClearFields"/>) and what Delete asks;
/// its list is read, checked and written through its editor (<see cref="ILookupListEditor{TItem}"/>).
/// </summary>
/// <typeparam name="TItem">The kind of item the list holds.</typeparam>
public abstract class LookupListState<TItem> : ScreenState, IDisposable
    where TItem : class
{
    private readonly Func<TItem, int> _idOf;
    private readonly string _noun;
    private readonly SourceCache<TItem, int> _cache;
    private readonly ReadOnlyObservableCollection<TItem> _items;

    /// <param name="noun">What an item is called in the form's title: "Unit", "Category", "Role".</param>
    /// <param name="idOf">An item's id.</param>
    /// <param name="order">The order the list shows its items in.</param>
    /// <param name="editor">What reads the list, checks the form's item and writes it.</param>
    /// <param name="logger">Where a failure is logged.</param>
    protected LookupListState(string noun, Func<TItem, int> idOf, IComparer<TItem> order, ILookupListEditor<TItem> editor, ILogger logger)
        : base(logger)
    {
        _noun = noun;
        _idOf = idOf;
        Editor = editor;
        _cache = new SourceCache<TItem, int>(idOf);

        _cache.Connect()
            .SortAndBind(out _items, order)
            .Subscribe();

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var items = await Editor.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedItem is { } selected ? _idOf(selected) : (int?)null;

            // The selection is pointed at its refreshed copy, which leaves the form and its unsaved edits as they are.
            _cache.MergeFrom(items);
            SelectedItem = Items.FirstOrDefault(i => _idOf(i) == selectedId);
        }));

        NewCommand = ScreenCommand.Of(ClearForm);

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            // Checked before anything is asked (a tab's SaveAsync may ask first), and refused with why.
            var item = FormItem();
            Editor.EnsureValid(item);
            await SaveAsync(item);
        }));

        DeleteCommand = new ScreenCommand(
            () => RunWithConfirmationAsync(DeleteQuestion(SelectedItem!), async () =>
            {
                await Editor.DeleteAsync(_idOf(SelectedItem!), CancellationToken.None);
                await FinishSaveAsync();
            }),
            () => SelectedItem is not null);
    }

    /// <summary>The list, in its order.</summary>
    public ReadOnlyObservableCollection<TItem> Items => _items;

    private TItem? _selectedItem;

    /// <summary>
    /// The item picked in the list, which the form shows for editing; null while the form is a new one's. Only
    /// <see cref="Pick"/> fills the form from it: a load points it at its refreshed copy, and New clears it.
    /// </summary>
    public TItem? SelectedItem
    {
        get => _selectedItem;
        private set => RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    /// <summary>
    /// The user picks <paramref name="item"/> in the list: the form shows it for editing, unless it's the one showing,
    /// whose unsaved edits the form keeps (a grid telling of the row it already has picked).
    /// </summary>
    public void Pick(TItem? item)
    {
        if (item == SelectedItem)
        {
            return;
        }

        SelectedItem = item;
        if (item is not null)
        {
            EditingId = _idOf(item);
            LoadIntoForm(item);
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

    /// <summary>What reads the list, checks an item and writes it.</summary>
    protected ILookupListEditor<TItem> Editor { get; }

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
            await Editor.AddAsync(item, CancellationToken.None);
        }
        else
        {
            await Editor.UpdateAsync(item, CancellationToken.None);
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
