using Microsoft.Extensions.DependencyInjection;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// A read a screen makes, through what it reads with (a use case's queries, SavedProductReader), on the restored copy, on
/// the calling thread: what a screen's load waits for before it draws anything.
/// </summary>
/// <param name="name">Its name (<see cref="Scenario.Name"/>).</param>
/// <param name="prepare">What it reads with, given the app's services: the read, set up (its records picked).</param>
/// <param name="problem">What's wrong with what a run read, or null when it's what the fixture holds.</param>
public sealed class ReadScenario<T>(
    string name,
    Func<IServiceProvider, Task<Func<Task<T>>>> prepare,
    Func<T, string?> problem) : Scenario
{
    private ServiceProvider? _services;
    private Func<Task<T>>? _read;
    private T? _last;

    public override string Name => name;

    public override async Task RunAsync() => _last = await _read!();

    public override Task CheckAsync() =>
        _last is null ? throw new InvalidOperationException($"{Name} read nothing.")
        : problem(_last) is { } wrong ? throw new InvalidOperationException($"{Name}: {wrong}")
        : Task.CompletedTask;

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
        _read = await prepare(_services);
    }
}
