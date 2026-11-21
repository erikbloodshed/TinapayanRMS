namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// One operation the benchmarks time (Suites) and the budgets count (Benchmarks.Tests' BudgetTests), so the two always
/// measure the same thing: set up once on the run's databases, then run as often as asked. A run of a screen's operation
/// ends once the screen has settled, its background reads included.
/// </summary>
public abstract class Scenario : IAsyncDisposable
{
    /// <summary>Its name in the budgets and the comparison's table: what it is, then what it does (Pricing.ReadAll).</summary>
    public abstract string Name { get; }

    /// <summary>Picks its records and builds what it runs on, which isn't measured; and runs it once, checked (<see cref="CheckAsync"/>).</summary>
    public async Task SetUpAsync(BenchDatabases databases)
    {
        await PrepareAsync(databases);
        await BeforeRunAsync();
        await RunAsync();
        await CheckAsync();
    }

    /// <summary>
    /// Whether every run needs <see cref="BeforeRunAsync"/> first: BenchmarkDotNet then runs it once an iteration, as its
    /// iteration setup (Suites' SteppedBenchmarks), so each iteration is one run.
    /// </summary>
    public virtual bool NeedsBeforeRun => false;

    /// <summary>
    /// What a run needs afresh that isn't part of it, such as a record read again before its save, or the last run's page
    /// left: not measured.
    /// </summary>
    public virtual Task BeforeRunAsync() => Task.CompletedTask;

    /// <summary>The operation.</summary>
    public abstract Task RunAsync();

    /// <summary>
    /// Throws when the last run didn't do what it should. A screen shows its failures rather than throwing them, and a
    /// failed read is quick: without this, a broken scenario would time its error and look fast.
    /// </summary>
    public abstract Task CheckAsync();

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>What <see cref="SetUpAsync"/> does before the first run.</summary>
    protected abstract Task PrepareAsync(BenchDatabases databases);
}
