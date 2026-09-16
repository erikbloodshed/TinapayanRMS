using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A standalone sub-assembly in the Sub-Assemblies list, with the products that use it. "Used by" is kept
/// as text: CollectionMerge compares a row's scalar properties, so a changed list of products shows up
/// as a changed row.
/// </summary>
public sealed class StandaloneSubAssemblyRow(SubAssembly subAssembly, IReadOnlyList<string> usedBy)
{
    public SubAssembly SubAssembly { get; } = subAssembly;

    public int Id => SubAssembly.Id;

    public string Name => SubAssembly.Name;

    public string RoleName => SubAssembly.Role?.Name ?? string.Empty;

    public decimal TotalWeight => SubAssembly.TotalWeight;

    public decimal TotalCost => SubAssembly.TotalCost;

    /// <summary>The products linking it, e.g. "Cream Loaf, Cream Loaf Mini", or empty when none do.</summary>
    public string UsedByText { get; } = string.Join(", ", usedBy);

    /// <summary>The products linking it, by name. A method, not a property: CollectionMerge would compare a list's strings by length only.</summary>
    public IReadOnlyList<string> GetUsedBy() => usedBy;
}

/// <summary>
/// The Sub-Assemblies screen: standalone sub-assemblies, which no product owns. Products link them (and
/// follow every change saved here) or copy them, from their recipe on the Products screen. One Save writes
/// the name, the role and the lines together.
/// </summary>
public class SubAssembliesViewModel : ViewModelBase
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly SourceCache<StandaloneSubAssemblyRow, int> _rowCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _rows;

    /// <summary>The sub-assembly the form shows: 0 for a new one.</summary>
    private int _editingId;

    /// <summary>Counts form loads, so a load overtaken by a newer one drops its lines instead of adding them too.</summary>
    private int _formLoadVersion;

    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> Rows => _rows;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The form's lines, typed straight into the table, in grams.</summary>
    public ObservableCollection<IngredientLineRow> Lines { get; } = [];

    /// <summary>The line the table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewLine => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id), _formulaBase);

    /// <summary>The formula base as last worked out, for a line started in the add row.</summary>
    private decimal _formulaBase;

    private StandaloneSubAssemblyRow? _selectedRow;
    public StandaloneSubAssemblyRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    private IngredientLineRow? _selectedLine;
    public IngredientLineRow? SelectedLine
    {
        get => _selectedLine;
        set => this.RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private SubAssemblyRole? _editingRole;
    public SubAssemblyRole? EditingRole
    {
        get => _editingRole;
        set => this.RaiseAndSetIfChanged(ref _editingRole, value);
    }

    private string _headerTitle = "New sub-assembly";
    /// <summary>The saved name of the sub-assembly the form shows, or "New sub-assembly".</summary>
    public string HeaderTitle
    {
        get => _headerTitle;
        private set => this.RaiseAndSetIfChanged(ref _headerTitle, value);
    }

    private string _formulaBaseCaption = "Tick the ingredients that make 100% — usually the flour.";
    /// <summary>The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by.</summary>
    public string FormulaBaseCaption
    {
        get => _formulaBaseCaption;
        private set => this.RaiseAndSetIfChanged(ref _formulaBaseCaption, value);
    }

    private decimal _batchWeight;
    /// <summary>The form's batch: the sum of its lines' grams.</summary>
    public decimal BatchWeight
    {
        get => _batchWeight;
        private set => this.RaiseAndSetIfChanged(ref _batchWeight, value);
    }

    private decimal _batchCost;
    /// <summary>What the form's batch costs, at its ingredients' buffered costs.</summary>
    public decimal BatchCost
    {
        get => _batchCost;
        private set => this.RaiseAndSetIfChanged(ref _batchCost, value);
    }

    private decimal _costPerGram;
    public decimal CostPerGram
    {
        get => _costPerGram;
        private set => this.RaiseAndSetIfChanged(ref _costPerGram, value);
    }

    private string _usedByText = NotUsedText;
    /// <summary>Which products link the sub-assembly the form shows, and what that means for editing it.</summary>
    public string UsedByText
    {
        get => _usedByText;
        private set => this.RaiseAndSetIfChanged(ref _usedByText, value);
    }

    private const string NotUsedText = "No product uses it yet. Link or copy it from a product's recipe on the Products screen.";

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    public SubAssembliesViewModel(
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository lineRepository,
        ISubAssemblyRoleRepository roleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        ILogger logger)
        : base(logger)
    {
        _subAssemblyRepository = subAssemblyRepository;
        _lineRepository = lineRepository;
        _subAssemblyValidator = subAssemblyValidator;

        _rowCache.Connect()
            .SortAndBind(out _rows, SortExpressionComparer<StandaloneSubAssemblyRow>.Ascending(r => r.Name))
            .Subscribe();

        // Picking a sub-assembly loads it into the form; a refresh only re-points the selection at its
        // refreshed copy, so it leaves the form and its unsaved edits alone.
        this.WhenAnyValue(x => x.SelectedRow)
            .Where(_ => !IsRefreshing)
            .Select(row => Observable.FromAsync(() => LoadIntoFormAsync(row)))
            .Switch()
            .Subscribe();

        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id)));

        // Cost follows both an ingredient picked and grams typed.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => RecomputeTotals());

        // The formula's base: what's ticked, and how much of it there is.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IsFormulaBase)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => RecomputeFormulaBase());

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection and unsaved lines while picking up
        // what changed elsewhere: a repriced ingredient, a renamed role, a product that links one.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await roleRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var subAssemblies = await _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None);
            var linkedBy = await _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedRow?.Id;
            var roleId = EditingRole?.Id;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                _rowCache.MergeFrom(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [])));

                // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
                EditingRole = Roles.FirstOrDefault(r => r.Id == roleId) ?? (_editingId == 0 && roleId is null ? Roles.FirstOrDefault() : null);

                IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
                RecomputeTotals();

                SelectedRow = Rows.FirstOrDefault(r => r.Id == selectedId);
                if (_editingId > 0)
                {
                    ShowUsedBy(linkedBy.GetValueOrDefault(_editingId) ?? []);
                }
            });
        });

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var name = EditingName.Trim();
            var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingRole?.Id ?? 0 });
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var lines = IngredientLineRow.CompleteLines(Lines, name);
            if (lines.Count == 0)
            {
                ErrorMessage = $"Add the ingredients that go into {name}, in grams.";
                return;
            }

            var id = await _subAssemblyRepository.SaveStandaloneAsync(
                new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id },
                [.. lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                CancellationToken.None);

            // The list picks up the new totals; then the form reloads what was saved.
            await LoadCommand.Execute();
            var saved = Rows.FirstOrDefault(r => r.Id == id);
            ApplyRefresh(() => SelectedRow = saved);
            await LoadIntoFormAsync(saved);
            SuccessMessage = $"{name} was saved.";
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedRow!.Name}'? This can't be undone.", async () =>
            {
                await _subAssemblyRepository.DeleteStandaloneAsync(SelectedRow!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        RemoveLineCommand = ReactiveCommand.Create(
            () => { Lines.Remove(SelectedLine!); },
            this.WhenAnyValue(x => x.SelectedLine).Select(line => line is not null));
    }

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, the first role.</summary>
    private void ClearForm()
    {
        _formLoadVersion++;
        ApplyRefresh(() => SelectedRow = null);
        _editingId = 0;
        HeaderTitle = "New sub-assembly";
        EditingName = string.Empty;
        EditingRole = Roles.FirstOrDefault();
        SelectedLine = null;
        Lines.Clear();
        UsedByText = NotUsedText;
    }

    /// <summary>
    /// The picked sub-assembly's name, role and lines, fetched again so they're what was saved. A load
    /// overtaken by a newer one (another pick, New) is dropped. Nothing picked clears the form.
    /// </summary>
    private async Task LoadIntoFormAsync(StandaloneSubAssemblyRow? row)
    {
        if (row is null)
        {
            ClearForm();
            return;
        }

        var version = ++_formLoadVersion;
        var saved = await _lineRepository.GetForSubAssembliesAsync([row.Id], CancellationToken.None);
        if (version != _formLoadVersion)
        {
            return;
        }

        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        _editingId = row.Id;
        HeaderTitle = row.Name;
        EditingName = row.Name;
        EditingRole = Roles.FirstOrDefault(r => r.Id == row.SubAssembly.RoleId);
        SelectedLine = null;
        Lines.Clear();
        Lines.AddRange(saved.Select(l => new IngredientLineRow(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
        ShowUsedBy(row.GetUsedBy());
    }

    private void ShowUsedBy(IReadOnlyList<string> productNames)
    {
        UsedByText = productNames.Count == 0
            ? NotUsedText
            : $"Used by {string.Join(", ", productNames)}. They follow every change saved here, and it can't be deleted while they use it.";
    }

    /// <summary>The formula's base, given to every row so each reads its own percentage, and said in the caption.</summary>
    private void RecomputeFormulaBase()
    {
        var baseWeight = IngredientLineRow.ApplyFormulaBase([.. Lines]);
        _formulaBase = baseWeight;
        var totalWeight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        FormulaBaseCaption = baseWeight > 0
            ? $"Base {baseWeight:#,0.##} g · whole formula {totalWeight / baseWeight * 100m:#,0.#}%"
            : "Tick the ingredients that make 100% — usually the flour.";
    }

    private void RecomputeTotals()
    {
        BatchWeight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        BatchCost = Lines.Sum(l => l.Cost);
        CostPerGram = BatchWeight > 0 ? BatchCost / BatchWeight : 0m;
    }
}
