using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI;

/// <summary>An in-memory (not yet saved) sub-assembly line, shown in the lines editor grid.</summary>
public class SubAssemblyLineEdit(Ingredient ingredient, decimal grams)
{
    public Ingredient Ingredient { get; } = ingredient;

    public decimal Grams { get; } = grams;
}

public class SubAssembliesViewModel : ViewModelBase
{
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _lineRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly IValidator<SubAssemblyRole> _roleValidator;
    private readonly SourceCache<SubAssembly, int> _subAssemblyCache = new(s => s.Id);
    private readonly ReadOnlyObservableCollection<SubAssembly> _subAssemblies;
    private readonly SourceCache<SubAssemblyRole, int> _roleCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<SubAssemblyRole> _roles;

    public ReadOnlyObservableCollection<SubAssembly> SubAssemblies => _subAssemblies;

    public ReadOnlyObservableCollection<SubAssemblyRole> Roles => _roles;

    public ObservableCollection<Ingredient> Ingredients { get; } = [];

    public ObservableCollection<SubAssemblyLineEdit> LineEdits { get; } = [];

    private SubAssembly? _selectedSubAssembly;
    public SubAssembly? SelectedSubAssembly
    {
        get => _selectedSubAssembly;
        set => this.RaiseAndSetIfChanged(ref _selectedSubAssembly, value);
    }

    private int _editingId;

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

    private Ingredient? _editingLineIngredient;
    public Ingredient? EditingLineIngredient
    {
        get => _editingLineIngredient;
        set => this.RaiseAndSetIfChanged(ref _editingLineIngredient, value);
    }

    private decimal _editingLineGrams;
    public decimal EditingLineGrams
    {
        get => _editingLineGrams;
        set => this.RaiseAndSetIfChanged(ref _editingLineGrams, value);
    }

    private SubAssemblyLineEdit? _selectedLineEdit;
    public SubAssemblyLineEdit? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _showLinesEditor;
    public bool ShowLinesEditor => _showLinesEditor.Value;

    // Role management (embedded panel, independent of the selected sub-assembly).
    private SubAssemblyRole? _selectedRole;
    public SubAssemblyRole? SelectedRole
    {
        get => _selectedRole;
        set => this.RaiseAndSetIfChanged(ref _selectedRole, value);
    }

    private int _editingRoleId;

    private string _editingRoleName = string.Empty;
    public string EditingRoleName
    {
        get => _editingRoleName;
        set => this.RaiseAndSetIfChanged(ref _editingRoleName, value);
    }

    private decimal _editingRoleSortOrder;
    public decimal EditingRoleSortOrder
    {
        get => _editingRoleSortOrder;
        set => this.RaiseAndSetIfChanged(ref _editingRoleSortOrder, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveLinesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewRoleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveRoleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteRoleCommand { get; }

    public SubAssembliesViewModel(
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository lineRepository,
        ISubAssemblyRoleRepository roleRepository,
        IValidator<SubAssembly> subAssemblyValidator,
        IValidator<SubAssemblyRole> roleValidator,
        IIngredientRepository ingredientRepository,
        ILogger logger)
        : base(logger)
    {
        _subAssemblyRepository = subAssemblyRepository;
        _lineRepository = lineRepository;
        _roleRepository = roleRepository;
        _subAssemblyValidator = subAssemblyValidator;
        _roleValidator = roleValidator;

        _subAssemblyCache.Connect()
            .SortAndBind(out _subAssemblies, SortExpressionComparer<SubAssembly>.Ascending(s => s.Name))
            .Subscribe();

        _roleCache.Connect()
            .SortAndBind(out _roles, SortExpressionComparer<SubAssemblyRole>.Ascending(r => r.SortOrder))
            .Subscribe();

        _showLinesEditor = this.WhenAnyValue(x => x.SelectedSubAssembly)
            .Select(subAssembly => subAssembly is not null)
            .ToProperty(this, x => x.ShowLinesEditor);

        this.WhenAnyValue(x => x.SelectedSubAssembly)
            .Where(subAssembly => subAssembly is not null)
            .Subscribe(subAssembly => LoadIntoForm(subAssembly!));

        this.WhenAnyValue(x => x.SelectedSubAssembly)
            .Subscribe(_ =>
            {
                SelectedLineEdit = null;
                EditingLineIngredient = null;
                EditingLineGrams = 0m;
            });

        this.WhenAnyValue(x => x.SelectedSubAssembly)
            .Select(subAssembly => Observable.FromAsync(() => LoadLinesAsync(subAssembly)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedRole)
            .Where(role => role is not null)
            .Subscribe(role =>
            {
                _editingRoleId = role!.Id;
                EditingRoleName = role.Name;
                EditingRoleSortOrder = role.SortOrder;
            });

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await _roleRepository.GetAllAsync(CancellationToken.None);
            _roleCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(roles);
            });

            var ingredients = await ingredientRepository.GetAllAsync(CancellationToken.None);
            Ingredients.Clear();
            Ingredients.AddRange(ingredients.OrderBy(i => i.Name));

            var subAssemblies = await _subAssemblyRepository.GetAllAsync(CancellationToken.None);
            _subAssemblyCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(subAssemblies);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedSubAssembly = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingRole = Roles.FirstOrDefault();
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var subAssembly = new SubAssembly
            {
                Id = _editingId,
                Name = EditingName,
                RoleId = EditingRole?.Id ?? 0,
            };

            var validation = _subAssemblyValidator.Validate(subAssembly);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _subAssemblyRepository.AddAsync(subAssembly, CancellationToken.None);
            }
            else
            {
                await _subAssemblyRepository.UpdateAsync(subAssembly, CancellationToken.None);
            }

            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedSubAssembly!.Name}'? This can't be undone.", async () =>
            {
                await _subAssemblyRepository.DeleteAsync(SelectedSubAssembly!.Id, CancellationToken.None);
                SelectedSubAssembly = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedSubAssembly).Select(subAssembly => subAssembly is not null));

        AddLineCommand = ReactiveCommand.Create(
            () => LineEdits.Add(new SubAssemblyLineEdit(EditingLineIngredient!, EditingLineGrams)),
            this.WhenAnyValue(x => x.EditingLineIngredient, x => x.EditingLineGrams, (ingredient, grams) => ingredient is not null && grams > 0));

        RemoveLineCommand = ReactiveCommand.Create(
            () => { LineEdits.Remove(SelectedLineEdit!); },
            this.WhenAnyValue(x => x.SelectedLineEdit).Select(line => line is not null));

        SaveLinesCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var lines = LineEdits.Select(l => (l.Ingredient.Id, l.Grams)).ToList();
                await _lineRepository.SetLinesAsync(SelectedSubAssembly!.Id, lines, CancellationToken.None);
                await RefreshSelectedSubAssemblyAsync();
            }),
            this.WhenAnyValue(x => x.SelectedSubAssembly).Select(subAssembly => subAssembly is not null));

        NewRoleCommand = ReactiveCommand.Create(() =>
        {
            SelectedRole = null;
            _editingRoleId = 0;
            EditingRoleName = string.Empty;
            EditingRoleSortOrder = Roles.Count + 1;
        });

        SaveRoleCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var role = new SubAssemblyRole
            {
                Id = _editingRoleId,
                Name = EditingRoleName,
                SortOrder = (int)EditingRoleSortOrder,
            };

            var validation = _roleValidator.Validate(role);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingRoleId == 0)
            {
                await _roleRepository.AddAsync(role, CancellationToken.None);
            }
            else
            {
                await _roleRepository.UpdateAsync(role, CancellationToken.None);
            }

            var roles = await _roleRepository.GetAllAsync(CancellationToken.None);
            _roleCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(roles);
            });
            SelectedRole = null;
        }));

        DeleteRoleCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedRole!.Name}' role? This can't be undone.", async () =>
            {
                await _roleRepository.DeleteAsync(SelectedRole!.Id, CancellationToken.None);
                var roles = await _roleRepository.GetAllAsync(CancellationToken.None);
                _roleCache.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(roles);
                });
                SelectedRole = null;
            }),
            this.WhenAnyValue(x => x.SelectedRole).Select(role => role is not null));
    }

    private void LoadIntoForm(SubAssembly subAssembly)
    {
        _editingId = subAssembly.Id;
        EditingName = subAssembly.Name;
        EditingRole = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId);
    }

    private async Task LoadLinesAsync(SubAssembly? subAssembly)
    {
        LineEdits.Clear();

        if (subAssembly is null)
        {
            return;
        }

        var lines = await _lineRepository.GetForSubAssemblyAsync(subAssembly.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            LineEdits.Add(new SubAssemblyLineEdit(line.Ingredient!, line.Grams));
        }
    }

    /// <summary>
    /// Re-fetches the selected sub-assembly (its TotalWeight/TotalCost just changed)
    /// and pushes it back into the grid's cache and the selection, which also
    /// re-triggers the lines reload above.
    /// </summary>
    private async Task RefreshSelectedSubAssemblyAsync()
    {
        if (SelectedSubAssembly is null)
        {
            return;
        }

        var refreshed = await _subAssemblyRepository.GetByIdAsync(SelectedSubAssembly.Id, CancellationToken.None);
        if (refreshed is not null)
        {
            _subAssemblyCache.AddOrUpdate(refreshed);
            SelectedSubAssembly = refreshed;
        }
    }
}
