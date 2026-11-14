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

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Settings' Material Categories tab: the raw material categories, each for ingredients, made ingredients or packaging.
/// Picking a category loads it for renaming or retyping; New, or finishing a Save or Delete, clears the form, so
/// the next Save always adds a category rather than renaming the last one touched.
/// </summary>
public sealed class CategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    private RawMaterialCategory? _selectedCategory;
    public RawMaterialCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private RawMaterialCategoryType _editingType;
    /// <summary>Whether the category is for ingredients, made ingredients or packaging: a material only goes in a category of its own type.</summary>
    public RawMaterialCategoryType EditingType
    {
        get => _editingType;
        set => this.RaiseAndSetIfChanged(ref _editingType, value);
    }

    /// <summary>The Type picker's choices.</summary>
    public IReadOnlyList<RawMaterialCategoryType> Types { get; } = Enum.GetValues<RawMaterialCategoryType>();

    private readonly ObservableAsPropertyHelper<string> _formTitle;
    /// <summary>"New Category", or "Edit Category" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => _formTitle.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public CategoriesViewModel(IRawMaterialCategoryRepository categoryRepository, IValidator<RawMaterialCategory> categoryValidator, ILogger logger)
        : base(logger)
    {
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        _formTitle = this.WhenAnyValue(x => x.SelectedCategory)
            .Select(category => category is null ? "New Category" : "Edit Category")
            .ToProperty(this, x => x.FormTitle, "New Category");

        // Skips while LoadCommand merges, which only re-points the selection at its refreshed copy.
        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category =>
            {
                _editingId = category!.Id;
                EditingName = category.Name;
                EditingType = category.Type;
            });

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedCategory?.Id;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId);
            });
        }));

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new RawMaterialCategory { Id = _editingId, Name = EditingName.Trim(), Type = EditingType };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            if (_editingId == 0)
            {
                await _categoryRepository.AddAsync(category, CancellationToken.None);
            }
            else
            {
                await _categoryRepository.UpdateAsync(category, CancellationToken.None);
            }

            ClearForm();
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
    }

    private void ClearForm()
    {
        SelectedCategory = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingType = RawMaterialCategoryType.Ingredient;
    }

    public void Dispose() => _categoryCache.Dispose();
}
