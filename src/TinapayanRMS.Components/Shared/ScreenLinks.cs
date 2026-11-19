using System.Reactive.Linq;
using Microsoft.AspNetCore.Components;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Presentation.PriceSimulator;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Reports;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// One screen opening another: a Dependents tile (Raw Materials, Sub-Assemblies) or a
/// double-clicked row (Reports, the Price Simulator) opens its product on Products; Sub-Assemblies' New Product starts a product
/// there that links it; a Reports row, or a made ingredient's tile on Sub-Assemblies' Dependents, opens its material on Raw
/// Materials; a linked badge on Products, or a made ingredient's Made from badge, opens its sub-assembly on
/// Sub-Assemblies; and a Print on Products opens the Print dialog with that choice. One per page the window loads, started by
/// the layout. The record goes into the screen's address, and the screen opens it from there.
/// </summary>
public sealed class ScreenLinks : IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public ScreenLinks(
        NavigationManager navigation,
        RawMaterialsState rawMaterials,
        SubAssembliesState subAssemblies,
        ProductsState products,
        ReportsState reports,
        PriceSimulatorState priceSimulator,
        PrintDialogState print,
        PrintPreviews previews)
    {
        _subscriptions.Add(subAssemblies.Dependents.ProductOpenRequests
            .Merge(rawMaterials.Dependents.ProductOpenRequests)
            .Merge(reports.ProductOpenRequests)
            .Merge(priceSimulator.ProductOpenRequests)
            .Subscribe(productId => navigation.NavigateTo($"products/{productId}")));

        // Products waits for the load that showing it starts, so the sub-assembly is found as it's saved now.
        _subscriptions.Add(subAssemblies.NewProductRequests.Subscribe(subAssemblyId =>
        {
            products.StartNewProductFrom(subAssemblyId);
            navigation.NavigateTo("products");
        }));

        _subscriptions.Add(reports.RawMaterialOpenRequests
            .Merge(subAssemblies.Dependents.RawMaterialOpenRequests)
            .Subscribe(rawMaterialId => navigation.NavigateTo($"raw-materials/{rawMaterialId}")));

        _subscriptions.Add(products.SubAssemblyOpenRequests
            .Merge(rawMaterials.Recipe.SubAssemblyOpenRequests)
            .Subscribe(subAssemblyId => navigation.NavigateTo($"sub-assemblies/{subAssemblyId}")));

        _subscriptions.Add(products.PrintRequests.Subscribe(scope =>
        {
            print.Pick(scope);
            previews.ShowProducts();
        }));
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
