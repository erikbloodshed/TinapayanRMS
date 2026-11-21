using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UseCases.RawMaterials;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// A new price for the flour, saved as the Raw Materials screen saves it (IRawMaterialEditor.SaveAsync), on the copy that's
/// written to: the save's cost cascade (IngredientCostPropagator) reprices every made and linked ingredient using it, in
/// the save's transaction. Each run moves its brand prices between the fixture's and 10% over them, so every save changes
/// the cost and cascades. The save refuses a material saved since it was read, so it's read again before each run, which
/// isn't measured.
/// </summary>
public sealed class CascadeScenario : Scenario
{
    private ServiceProvider? _services;
    private IRawMaterialQueries _queries = null!;
    private IRawMaterialEditor _editor = null!;
    private int _flourId;
    private IReadOnlyList<(string? Brand, int? UnitOfMeasureId, decimal Price)> _fixturePrices = [];
    private IReadOnlyList<(string? Brand, int? UnitOfMeasureId, decimal Price)> _raisedPrices = [];
    private bool _raise = true;
    private RawMaterialSaveRequest? _request;
    private decimal _costBefore;

    public override string Name => "Cascade.SaveFlourPrice";

    public override bool NeedsBeforeRun => true;

    public override async Task BeforeRunAsync()
    {
        var flour = await _queries.ReadAsync(_flourId, CancellationToken.None)
            ?? throw new InvalidOperationException($"{FixtureRecords.Flour} has gone.");
        _costBefore = flour.CostPerBaseUnit;

        // No buffered cost sent: the save works it out from the new prices, as a first price does.
        flour.BufferedCostPerBaseUnit = 0m;
        _request = new RawMaterialSaveRequest(flour, flour.Units, _raise ? _raisedPrices : _fixturePrices, null);
        _raise = !_raise;
    }

    public override Task RunAsync() => _editor.SaveAsync(_request!, CancellationToken.None);

    public override async Task CheckAsync()
    {
        var flour = await _queries.ReadAsync(_flourId, CancellationToken.None);
        Screens.Ensure(flour is not null && flour.CostPerBaseUnit != _costBefore, $"{Name}: the save left {FixtureRecords.Flour}'s cost as it was.");
    }

    public override async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override async Task PrepareAsync(BenchDatabases databases)
    {
        _services = AppServices.Build(databases.Write);
        _queries = _services.GetRequiredService<IRawMaterialQueries>();
        _editor = _services.GetRequiredService<IRawMaterialEditor>();

        var flour = await FixtureRecords.MaterialAsync(_services, FixtureRecords.Flour);
        Screens.Ensure(flour.Source == RawMaterialSource.Purchased, $"{FixtureRecords.Flour} isn't a purchased material.");
        _flourId = flour.Id;

        var saved = (await _queries.ReadAsync(_flourId, CancellationToken.None))!;
        var detail = await _queries.ReadDetailAsync(saved, CancellationToken.None);
        Screens.Ensure(detail.BrandPrices.Count > 0, $"{FixtureRecords.Flour} has no price to change.");
        _fixturePrices = [.. detail.BrandPrices.Select(p => (p.Brand, UnitOfMeasureOf(saved, p), p.Price))];
        _raisedPrices = [.. detail.BrandPrices.Select(p => (p.Brand, UnitOfMeasureOf(saved, p), decimal.Round(p.Price * 1.1m, 2)))];
    }

    /// <summary>The unit of measure a brand price is in, as a save names it: null for one in the base unit.</summary>
    private static int? UnitOfMeasureOf(RawMaterial material, RawMaterialBrandPrice price) =>
        price.UnitId is { } unitId ? material.Units.Single(u => u.Id == unitId).UnitOfMeasureId : null;
}
