using System.Diagnostics;
using System.Reactive;
using System.Reactive.Subjects;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.Trees;

/// <summary>
/// The writer that keeps a list's folders between runs, on its own: a subject stands in for the tree's ExpansionChanged
/// and a list for its open folders. The screens' use of it is in their *StateTreeTests.
/// </summary>
public class FolderStateWriterTests
{
    private const string Key = "Test.OpenFolders";

    [Test]
    public void Dispose_NothingOpenedOrClosed_WritesNothing()
    {
        var store = new InMemoryViewStateStore();
        using var changed = new Subject<Unit>();
        List<string> open = ["Production"];

        new FolderStateWriter(store, Key, changed, () => [.. open]).Dispose();

        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
    }

    [Test]
    public void Dispose_AChangeStillSettling_WritesItNow()
    {
        var store = new InMemoryViewStateStore();
        using var changed = new Subject<Unit>();
        List<string> open = ["Production"];
        var writer = new FolderStateWriter(store, Key, changed, () => [.. open]);

        open.Add("Production/12");
        changed.OnNext(Unit.Default);
        writer.Dispose();

        store.ReadList(Key).Should().Equal("Production", "Production/12");
        store.WriteCount.Should().Be(1);
    }

    [Test]
    public Task ExpansionChanged_SettledChanges_WritesTheLastOnceAfterThem() => UiThread.RunAsync(async () =>
    {
        var store = new InMemoryViewStateStore();
        using var changed = new Subject<Unit>();
        List<string> open = [];
        using var writer = new FolderStateWriter(store, Key, changed, () => [.. open]);

        // Expand All opens several in a row: one write, of where they ended up.
        foreach (var folder in (string[])["Production", "Production/12", "Production/Uncategorized"])
        {
            open.Add(folder);
            changed.OnNext(Unit.Default);
        }

        await WaitForAsync(() => store.WriteCount > 0);
        store.ReadList(Key).Should().Equal("Production", "Production/12", "Production/Uncategorized");
        store.WriteCount.Should().Be(1);
    });

    [Test]
    public Task ExpansionChanged_BackAsItWas_WritesNothing() => UiThread.RunAsync(async () =>
    {
        var store = new InMemoryViewStateStore();
        using var changed = new Subject<Unit>();
        List<string> open = ["Production"];
        var writer = new FolderStateWriter(store, Key, changed, () => [.. open]);

        // Closed and opened again before it settled: the same folders as were read.
        open.Clear();
        changed.OnNext(Unit.Default);
        open.Add("Production");
        changed.OnNext(Unit.Default);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current!.Execution.CancellationToken);
        writer.Dispose();

        store.WriteCount.Should().Be(0);
    });

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException("Nothing was written.");
            }

            await Task.Delay(25);
        }
    }
}
