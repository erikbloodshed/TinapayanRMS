using TinapayanRMS.Benchmarks.Scenarios;
using TUnit.Core.Interfaces;

namespace TinapayanRMS.Benchmarks.Tests.Fixtures;

/// <summary>
/// The benchmarks' databases (BenchDatabases: the golden master's copy of the shop's data, restored, and a copy of it to
/// write to), restored once for the budgets and dropped after them. Databases of their own (tinapayanrms_bench_), so they
/// need no test database or reset.
/// </summary>
public sealed class BudgetDatabases : IAsyncInitializer, IAsyncDisposable
{
    private BenchDatabases? _databases;

    public BenchDatabases Databases => _databases ?? throw new InvalidOperationException("Not restored yet.");

    public async Task InitializeAsync() => _databases = await BenchDatabases.CreateAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_databases is not null)
        {
            await _databases.DisposeAsync();
        }

        AppServices.DeleteRunFolder();
    }
}
