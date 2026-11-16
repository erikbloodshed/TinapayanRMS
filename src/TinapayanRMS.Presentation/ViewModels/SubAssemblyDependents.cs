using System.Reactive.Subjects;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Sub-Assemblies screen's Dependents tab (through SubAssembliesViewModel.Dependents): the products linking the
/// sub-assembly the form shows and the made ingredients made from it, one tile each, which opens it, and what linking means
/// for editing it. The screen says what to show as each sub-assembly is loaded.
/// </summary>
public sealed class SubAssemblyDependents : ObservableState, IDisposable
{
    private const string NotUsedText = "No product uses it yet. Link or copy it from a product's recipe on the Products screen.";

    private const string SetNotUsedText = "No product uses it yet. Link it from a product's Recipe Structure: Add Sub-Assembly, Link an existing one.";

    private const string UsedText = "These products follow every change saved here, and it can't be deleted while they use it.";

    private const string MadeFromText = "These follow every change saved here, and it can't be deleted while they use it: the products link it, and the made ingredients are made from it.";

    private readonly Subject<int> _productOpenRequests = new();
    private readonly Subject<int> _rawMaterialOpenRequests = new();

    public SubAssemblyDependents()
    {
        OpenProductCommand = ScreenCommand.Of<int>(_productOpenRequests.OnNext);
        OpenRawMaterialCommand = ScreenCommand.Of<int>(_rawMaterialOpenRequests.OnNext);
    }

    private IReadOnlyList<LinkingProduct> _products = [];
    /// <summary>The products linking the sub-assembly the form shows, by name: one tile each, which opens it.</summary>
    public IReadOnlyList<LinkingProduct> Products
    {
        get => _products;
        private set => RaiseAndSetIfChanged(ref _products, value);
    }

    private IReadOnlyList<MadeIngredientUse> _madeIngredients = [];
    /// <summary>
    /// The made ingredients made from the sub-assembly the form shows, by name: one tile each, after the products, which
    /// opens it on Raw Materials.
    /// </summary>
    public IReadOnlyList<MadeIngredientUse> MadeIngredients
    {
        get => _madeIngredients;
        private set => RaiseAndSetIfChanged(ref _madeIngredients, value);
    }

    private string _text = NotUsedText;
    /// <summary>The caption under the tiles: what linking means for editing it, or how to link it when nothing does.</summary>
    public string Text
    {
        get => _text;
        private set => RaiseAndSetIfChanged(ref _text, value);
    }

    /// <summary>A product's tile: asks for it to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ScreenCommand<int> OpenProductCommand { get; }

    /// <summary>A made ingredient's tile: asks for it to be opened on Raw Materials, through <see cref="RawMaterialOpenRequests"/>.</summary>
    public ScreenCommand<int> OpenRawMaterialCommand { get; }

    /// <summary>
    /// The products the tiles ask to open, by id. ScreenLinks subscribes and opens each on the Products screen. Raised as the
    /// tile is clicked, not on the main-thread scheduler a command's own results wait for.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The made ingredients the tiles ask to open, by id. ScreenLinks subscribes and opens each on Raw Materials.</summary>
    public IObservable<int> RawMaterialOpenRequests => _rawMaterialOpenRequests;

    /// <summary>
    /// What uses the sub-assembly the form shows: <paramref name="products"/> linking it and <paramref name="madeIngredients"/>
    /// made from it. <paramref name="isPackagingSet"/> says how one nothing uses yet is linked.
    /// </summary>
    public void Show(IReadOnlyList<LinkingProduct> products, IReadOnlyList<MadeIngredientUse> madeIngredients, bool isPackagingSet)
    {
        Products = products;
        MadeIngredients = madeIngredients;
        Text = madeIngredients.Count > 0 ? MadeFromText
            : products.Count == 0 ? isPackagingSet ? SetNotUsedText : NotUsedText
            : UsedText;
    }

    public void Dispose()
    {
        _productOpenRequests.Dispose();
        _rawMaterialOpenRequests.Dispose();
    }
}
