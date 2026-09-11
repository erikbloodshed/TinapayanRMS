using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI;

public class IngredientsViewModel : ReactiveObject
{
    private readonly IIngredientRepository _ingredientRepository;
    private readonly SourceCache<Ingredient, int> _ingredientCache = new(i => i.Id);
    private readonly ReadOnlyObservableCollection<Ingredient> _ingredients;

    public ReadOnlyObservableCollection<Ingredient> Ingredients => _ingredients;

    public ObservableCollection<IngredientCategory> Categories { get; } = [];

    public ObservableCollection<UnitOfMeasure> PurchaseUnits { get; } = [];

    public IReadOnlyList<IngredientSource> Sources { get; } = Enum.GetValues<IngredientSource>();

    private Ingredient? _selectedIngredient;
    public Ingredient? SelectedIngredient
    {
        get => _selectedIngredient;
        set => this.RaiseAndSetIfChanged(ref _selectedIngredient, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private IngredientCategory? _editingCategory;
    public IngredientCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private IngredientSource _editingSource = IngredientSource.Purchased;
    public IngredientSource EditingSource
    {
        get => _editingSource;
        set => this.RaiseAndSetIfChanged(ref _editingSource, value);
    }

    private UnitOfMeasure? _editingPurchaseUnit;
    public UnitOfMeasure? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    private decimal? _editingPurchaseUnitQty;
    public decimal? EditingPurchaseUnitQty
    {
        get => _editingPurchaseUnitQty;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnitQty, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isEditingPurchased;
    public bool IsEditingPurchased => _isEditingPurchased.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public IngredientsViewModel(
        IIngredientRepository ingredientRepository,
        ILookupRepository<IngredientCategory> categoryRepository,
        ILookupRepository<UnitOfMeasure> unitOfMeasureRepository)
    {
        _ingredientRepository = ingredientRepository;

        _ingredientCache.Connect()
            .SortAndBind(out _ingredients, SortExpressionComparer<Ingredient>.Ascending(i => i.Name))
            .Subscribe();

        _isEditingPurchased = this.WhenAnyValue(x => x.EditingSource)
            .Select(source => source == IngredientSource.Purchased)
            .ToProperty(this, x => x.IsEditingPurchased);

        this.WhenAnyValue(x => x.SelectedIngredient)
            .Where(ingredient => ingredient is not null)
            .Subscribe(ingredient => LoadIntoForm(ingredient!));

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await categoryRepository.GetAllAsync(CancellationToken.None);
            Categories.Clear();
            Categories.AddRange(categories.OrderBy(c => c.Name));

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            PurchaseUnits.Clear();
            PurchaseUnits.AddRange(units.OrderBy(u => u.Name));

            var ingredients = await _ingredientRepository.GetAllAsync(CancellationToken.None);
            _ingredientCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(ingredients);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedIngredient = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingSource = IngredientSource.Purchased;
            EditingPurchaseUnit = PurchaseUnits.FirstOrDefault();
            EditingPurchaseUnitQty = null;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var ingredient = new Ingredient
            {
                Id = _editingId,
                Name = EditingName,
                IngredientCategoryId = EditingCategory?.Id ?? 0,
                Source = EditingSource,
                PurchaseUnitId = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnit?.Id : null,
                PurchaseUnitQty = EditingSource == IngredientSource.Purchased ? EditingPurchaseUnitQty : null,
            };

            if (_editingId == 0)
            {
                await _ingredientRepository.AddAsync(ingredient, CancellationToken.None);
            }
            else
            {
                await _ingredientRepository.UpdateAsync(ingredient, CancellationToken.None);
            }

            await LoadCommand.Execute();
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _ingredientRepository.DeleteAsync(SelectedIngredient!.Id, CancellationToken.None);
                SelectedIngredient = null;
                await LoadCommand.Execute();
            },
            this.WhenAnyValue(x => x.SelectedIngredient).Select(ingredient => ingredient is not null));
    }

    private void LoadIntoForm(Ingredient ingredient)
    {
        _editingId = ingredient.Id;
        EditingName = ingredient.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == ingredient.IngredientCategoryId);
        EditingSource = ingredient.Source;
        EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == ingredient.PurchaseUnitId);
        EditingPurchaseUnitQty = ingredient.PurchaseUnitQty;
    }
}
