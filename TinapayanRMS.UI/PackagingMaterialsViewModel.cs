using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI;

public class PackagingMaterialsViewModel : ReactiveObject
{
    private readonly IPackagingMaterialRepository _packagingMaterialRepository;
    private readonly SourceCache<PackagingMaterial, int> _packagingMaterialCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<PackagingMaterial> _packagingMaterials;

    public ReadOnlyObservableCollection<PackagingMaterial> PackagingMaterials => _packagingMaterials;

    public ObservableCollection<PackagingCategory> Categories { get; } = [];

    public ObservableCollection<UnitOfMeasure> StandardUnits { get; } = [];

    public ObservableCollection<UnitOfMeasure> PurchaseUnits { get; } = [];

    private PackagingMaterial? _selectedPackagingMaterial;
    public PackagingMaterial? SelectedPackagingMaterial
    {
        get => _selectedPackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedPackagingMaterial, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private PackagingCategory? _editingCategory;
    public PackagingCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private UnitOfMeasure? _editingStandardUnit;
    public UnitOfMeasure? EditingStandardUnit
    {
        get => _editingStandardUnit;
        set => this.RaiseAndSetIfChanged(ref _editingStandardUnit, value);
    }

    private UnitOfMeasure? _editingPurchaseUnit;
    public UnitOfMeasure? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    private decimal _editingPurchaseUnitQty;
    public decimal EditingPurchaseUnitQty
    {
        get => _editingPurchaseUnitQty;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnitQty, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public PackagingMaterialsViewModel(
        IPackagingMaterialRepository packagingMaterialRepository,
        ILookupRepository<PackagingCategory> categoryRepository,
        ILookupRepository<UnitOfMeasure> unitOfMeasureRepository)
    {
        _packagingMaterialRepository = packagingMaterialRepository;

        _packagingMaterialCache.Connect()
            .SortAndBind(out _packagingMaterials, SortExpressionComparer<PackagingMaterial>.Ascending(p => p.Name))
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Where(packagingMaterial => packagingMaterial is not null)
            .Subscribe(packagingMaterial => LoadIntoForm(packagingMaterial!));

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await categoryRepository.GetAllAsync(CancellationToken.None);
            Categories.Clear();
            Categories.AddRange(categories.OrderBy(c => c.Name));

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var orderedUnits = units.OrderBy(u => u.Name).ToList();

            StandardUnits.Clear();
            StandardUnits.AddRange(orderedUnits.Where(u => PackagingMaterialRules.IsValidStandardUnit(u.Id)));

            PurchaseUnits.Clear();
            PurchaseUnits.AddRange(orderedUnits);

            var packagingMaterials = await _packagingMaterialRepository.GetAllAsync(CancellationToken.None);
            _packagingMaterialCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(packagingMaterials);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedPackagingMaterial = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingStandardUnit = StandardUnits.FirstOrDefault();
            EditingPurchaseUnit = PurchaseUnits.FirstOrDefault();
            EditingPurchaseUnitQty = 0;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var packagingMaterial = new PackagingMaterial
            {
                Id = _editingId,
                Name = EditingName,
                PackagingCategoryId = EditingCategory?.Id ?? 0,
                StandardUnitId = EditingStandardUnit?.Id ?? 0,
                PurchaseUnitId = EditingPurchaseUnit?.Id ?? 0,
                PurchaseUnitQty = EditingPurchaseUnitQty,
            };

            if (_editingId == 0)
            {
                await _packagingMaterialRepository.AddAsync(packagingMaterial, CancellationToken.None);
            }
            else
            {
                await _packagingMaterialRepository.UpdateAsync(packagingMaterial, CancellationToken.None);
            }

            await LoadCommand.Execute();
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _packagingMaterialRepository.DeleteAsync(SelectedPackagingMaterial!.Id, CancellationToken.None);
                SelectedPackagingMaterial = null;
                await LoadCommand.Execute();
            },
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(packagingMaterial => packagingMaterial is not null));
    }

    private void LoadIntoForm(PackagingMaterial packagingMaterial)
    {
        _editingId = packagingMaterial.Id;
        EditingName = packagingMaterial.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == packagingMaterial.PackagingCategoryId);
        EditingStandardUnit = StandardUnits.FirstOrDefault(u => u.Id == packagingMaterial.StandardUnitId);
        EditingPurchaseUnit = PurchaseUnits.FirstOrDefault(u => u.Id == packagingMaterial.PurchaseUnitId);
        EditingPurchaseUnitQty = packagingMaterial.PurchaseUnitQty;
    }
}
