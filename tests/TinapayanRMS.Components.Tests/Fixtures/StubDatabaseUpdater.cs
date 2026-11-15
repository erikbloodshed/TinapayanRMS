using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>
/// The database made ready, for the component tests, which have no database: up to date at once, or, with
/// <see cref="Updates"/> set, applying that many until <see cref="Release"/> lets it finish.
/// </summary>
public sealed class StubDatabaseUpdater : IDatabaseUpdater
{
    public int Updates { get; init; }

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken)
    {
        if (Updates == 0)
        {
            return new DatabaseUpdate(0, null, Created: false);
        }

        applying?.Invoke(Updates);
        await Release.Task;
        return new DatabaseUpdate(Updates, null, Created: false);
    }
}
