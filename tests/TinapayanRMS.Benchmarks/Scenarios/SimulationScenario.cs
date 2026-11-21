using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.UseCases.PriceSimulator;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The Price Simulator's working out (PriceChangeSimulation.Run) of the golden master's four price changes over every
/// product, from what its load read, which isn't measured: the costing alone, what the screen works out again at each
/// change typed into its table, with no database.
/// </summary>
public sealed class SimulationScenario : Scenario
{
    private ServiceProvider? _services;
    private PriceSimulatorRead _read = null!;
    private IReadOnlyDictionary<int, RawMaterial> _materials = null!;
    private IReadOnlyDictionary<int, (decimal Cost, decimal BufferedCost)> _newCosts = null!;
    private SimulatedPrices? _last;

    public override string Name => "PriceSimulator.Simulate";

    public override Task RunAsync()
    {
        _last = PriceChangeSimulation.Run(_read.Products, _read.Details, _materials, _read.BomLines, _newCosts, CostBasis.Buffered);
        return Task.CompletedTask;
    }

    public override Task CheckAsync()
    {
        Screens.Ensure(
            _last is not null && _last.Products.Count == _read.Products.Count && _last.ReachedBy.Count == FixtureRecords.PriceChanges.Count,
            $"{Name} didn't price every product with every change.");
        return Task.CompletedTask;
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
        _services = AppServices.Build(databases.Read);
        _read = await _services.GetRequiredService<IPriceSimulatorQueries>().ReadAsync(CancellationToken.None);
        _materials = _read.Materials.ToDictionary(m => m.Id);
        Dictionary<int, (decimal Cost, decimal BufferedCost)> newCosts = [];
        foreach (var (name, percent) in FixtureRecords.PriceChanges)
        {
            var material = _read.Materials.Single(m => m.Name == name);
            var factor = 1m + (percent / 100m);
            newCosts[material.Id] = (material.CostPerBaseUnit * factor, material.BufferedCostPerBaseUnit * factor);
        }

        _newCosts = newCosts;
    }
}
