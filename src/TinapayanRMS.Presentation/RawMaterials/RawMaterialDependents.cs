using System.Reactive;
using System.Reactive.Subjects;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// The Raw Materials screen's Dependents tab (through RawMaterialsViewModel.Dependents): the products the material showing
/// goes into, one tile each, which opens it, with what the material makes up of each one's material cost once the tab shows.
/// The screen says what to show as each material is loaded, and when to work the shares out.
/// </summary>
public sealed class RawMaterialDependents : ObservableState, IDisposable
{
    private const string NotUsedText = "No product uses it yet.";

    private const string UsedText =
        "These products use it in their recipe or packaging, directly or through a made or linked ingredient, so a change to its cost reaches each of them.";

    private readonly MaterialCostShares? _costShares;
    private readonly Subject<int> _productOpenRequests = new();
    private readonly Subject<Unit> _sharesWanted = new();

    /// <param name="costShares">What each product's share is worked out by; null in a screen built without it (tests that don't need it).</param>
    public RawMaterialDependents(MaterialCostShares? costShares)
    {
        _costShares = costShares;
        OpenProductCommand = ScreenCommand.Of<int>(_productOpenRequests.OnNext);
    }

    /// <summary>
    /// The products the material showing goes into, by name (IRawMaterialRepository.GetDependentsAsync): one tile each, which
    /// opens it. Empty for a new material.
    /// </summary>
    public IReadOnlyList<LinkingProduct> Products
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = [];

    /// <summary>The caption under the tiles: what they mean for a change of cost, or that nothing uses the material.</summary>
    public string Text
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = NotUsedText;

    /// <summary>A tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ScreenCommand<int> OpenProductCommand { get; }

    /// <summary>
    /// The products the tiles ask to open, by id. ScreenLinks subscribes and opens each on the Products screen, as it does
    /// for Sub-Assemblies' tiles.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The tiles changed, so their shares are wanted again, once the tab shows.</summary>
    public IObservable<Unit> SharesWanted => _sharesWanted;

    /// <summary>What uses the material showing: <paramref name="products"/>, by name, their shares still to be worked out.</summary>
    public void Show(IReadOnlyList<LinkingProduct> products)
    {
        Products = products;
        Text = products.Count == 0 ? NotUsedText : UsedText;
        _sharesWanted.OnNext(Unit.Default);
    }

    /// <summary>
    /// The tiles' "38% of materials", for material <paramref name="materialId"/>: worked out only while the tab shows, since it
    /// reads every product the material goes into (MaterialCostShares). Kept on the tiles until they change.
    /// </summary>
    public async Task ShowCostSharesAsync(int materialId, CancellationToken cancellationToken)
    {
        if (_costShares is not { } costShares || Products is not { Count: > 0 } tiles || tiles.All(t => t.CostShareText is not null))
        {
            return;
        }

        var shares = await Task.Run(() => costShares.ReadAsync(materialId, [.. tiles.Select(t => t.Id)], cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (ReferenceEquals(Products, tiles))
        {
            Products = [.. tiles.Select(t => t with { CostShareText = MaterialCostShares.Text(shares.GetValueOrDefault(t.Id)) })];
        }
    }

    public void Dispose()
    {
        _productOpenRequests.Dispose();
        _sharesWanted.Dispose();
    }
}
