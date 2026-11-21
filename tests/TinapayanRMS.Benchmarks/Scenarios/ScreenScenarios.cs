using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// A record view opened as its page's first visit opens it: the view made for a new page (its scope), its load, and with
/// <paramref name="openRecord"/>, the record its address names opened in a tab; until it has settled, the screen's
/// background reads included. On the UI thread, as in the window. The last run's page goes before the next run, which
/// isn't measured.
/// </summary>
/// <param name="name">Its name (<see cref="Scenario.Name"/>).</param>
/// <param name="openRecord">The record to open, picked from the app's services, or null to open the view on its list alone.</param>
/// <param name="problem">What's wrong with what the view shows after a run, or null when it's what the fixture holds.</param>
public sealed class OpenViewScenario<TView, TForm>(
    string name,
    Func<IServiceProvider, Task<int>>? openRecord,
    Func<TView, int?, string?> problem) : Scenario
    where TView : RecordViewState<TForm>
    where TForm : ScreenState, ITabForm, IDisposable
{
    private UiThread? _ui;
    private ServiceProvider? _services;
    private AsyncServiceScope? _page;
    private TView? _view;
    private int? _recordId;

    public override string Name => name;

    public override bool NeedsBeforeRun => true;

    private UiThread Ui => _ui ?? throw new InvalidOperationException($"{Name} isn't set up.");

    public override Task BeforeRunAsync() => Ui.RunAsync(LeavePageAsync);

    public override Task RunAsync() => Ui.RunAsync(async () =>
    {
        _page = _services!.CreateAsyncScope();
        _view = _page.Value.ServiceProvider.GetRequiredService<TView>();
        await _view.LoadCommand.ExecuteAsync();
        await Screens.SettleAsync(_view);
        if (_recordId is { } id)
        {
            _view.OpenRecord(id);
            await Screens.SettleAsync(_view);
        }
    });

    public override Task CheckAsync() => Ui.RunAsync(() =>
    {
        Screens.EnsureNoFailure(_view);
        Screens.EnsureNoFailure(_view?.Form);
        var wrong = _view is null ? "nothing opened" : problem(_view, _recordId);
        Screens.Ensure(wrong is null, $"{Name}: {wrong}");
        return Task.CompletedTask;
    });

    public override async ValueTask DisposeAsync()
    {
        if (_ui is { } ui)
        {
            await ui.RunAsync(LeavePageAsync);
            ui.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override async Task PrepareAsync(BenchDatabases databases)
    {
        _ui = new UiThread();
        _services = AppServices.Build(databases.Read);
        _recordId = openRecord is null ? null : await openRecord(_services);
    }

    private async Task LeavePageAsync()
    {
        if (_page is { } page)
        {
            _page = null;
            _view = null;
            await page.DisposeAsync();
        }
    }
}

/// <summary>
/// One product picked after another in an open Products view, as a click on the list's row picks it: the preview tab
/// shows it, its details read and drawn, until the screen has settled. The two products with the most lines, in turn.
/// </summary>
public sealed class PickProductScenario : Scenario
{
    private UiThread? _ui;
    private ServiceProvider? _services;
    private AsyncServiceScope? _page;
    private ProductsState _view = null!;
    private Product[] _products = [];
    private int _next;
    private int _picked;

    public override string Name => "Screens.PickProduct";

    private UiThread Ui => _ui ?? throw new InvalidOperationException($"{Name} isn't set up.");

    public override Task RunAsync() => Ui.RunAsync(async () =>
    {
        _picked = _products[_next].Id;
        _next = 1 - _next;
        _view.OpenRecord(_picked);
        await Screens.SettleAsync(_view);
    });

    public override Task CheckAsync() => Ui.RunAsync(() =>
    {
        Screens.EnsureNoFailure(_view);
        Screens.EnsureNoFailure(_view.Form);
        Screens.Ensure(
            _view.Form is { AreDetailsReady: true } form && form.SelectedProduct?.Id == _picked,
            $"{Name}: the form doesn't show product {_picked}.");
        return Task.CompletedTask;
    });

    public override async ValueTask DisposeAsync()
    {
        if (_ui is { } ui)
        {
            await ui.RunAsync(async () =>
            {
                if (_page is { } page)
                {
                    await page.DisposeAsync();
                }
            });
            ui.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override async Task PrepareAsync(BenchDatabases databases)
    {
        _ui = new UiThread();
        _services = AppServices.Build(databases.Read);
        _products = [.. (await FixtureRecords.ProductsByWeightAsync(_services)).Take(2)];
        await _ui.RunAsync(async () =>
        {
            var page = _services.CreateAsyncScope();
            _page = page;
            _view = page.ServiceProvider.GetRequiredService<ProductsState>();
            await _view.LoadCommand.ExecuteAsync();
            await Screens.SettleAsync(_view);
        });
    }
}

/// <summary>The record views' scenarios.</summary>
public static class ScreenScenarios
{
    /// <summary>Products, opened on the product with the most lines.</summary>
    public static Scenario OpenProduct() => new OpenViewScenario<ProductsState, ProductForm>(
        "Screens.OpenProduct",
        async services => (await FixtureRecords.ProductsByWeightAsync(services))[0].Id,
        (view, id) => view.Products.Count == 0 ? "the list is empty"
            : view.Form is not { AreDetailsReady: true } form || form.SelectedProduct?.Id != id ? $"the form doesn't show product {id}"
            : null);

    /// <summary>Raw Materials, opened on its list: the screen the app opens on.</summary>
    public static Scenario OpenRawMaterials() => new OpenViewScenario<RawMaterialsState, RawMaterialForm>(
        "Screens.OpenRawMaterials",
        null,
        (view, _) => view.RawMaterials.Count == 0 ? "the list is empty" : null);

    /// <summary>Sub-Assemblies, opened on its list.</summary>
    public static Scenario OpenSubAssemblies() => new OpenViewScenario<SubAssembliesState, SubAssemblyForm>(
        "Screens.OpenSubAssemblies",
        null,
        (view, _) => view.List.Rows.Count == 0 ? "the list is empty" : null);
}
