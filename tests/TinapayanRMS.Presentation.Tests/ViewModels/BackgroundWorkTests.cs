using AwesomeAssertions;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>The work a screen starts that nothing awaits, and waiting for it to settle.</summary>
public class BackgroundWorkTests
{
    private readonly BackgroundWork _work = new();

    [Test]
    public Task Work_is_counted_from_its_start_until_it_ends_however_it_ends() => UiThread.RunAsync(async () =>
    {
        var read = new TaskCompletionSource();
        var started = _work.RunAsync(() => read.Task);
        _work.Running.Should().Be(1, "counted before its first await");

        var failing = _work.RunAsync(() => Task.FromException(new InvalidOperationException("refused")));
        await failing.Invoking(t => t).Should().ThrowAsync<InvalidOperationException>();
        _work.Running.Should().Be(1, "work that fails is done");

        read.SetResult();
        await started;
        _work.Running.Should().Be(0);
    });

    [Test]
    public Task Idle_waits_for_work_and_for_the_work_it_starts_on_the_way() => UiThread.RunAsync(async () =>
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var finished = new List<string>();
        _ = _work.RunAsync(async () =>
        {
            await first.Task;
            finished.Add("first");

            // Its continuation starts more, as a read's result can (a line priced, then its cost read).
            _ = _work.RunAsync(async () =>
            {
                await second.Task;
                finished.Add("second");
            });
        });

        var idle = _work.WhenIdleAsync();
        first.SetResult();
        await Task.Delay(50);
        idle.IsCompleted.Should().BeFalse("the work the first started is still running");

        second.SetResult();
        await idle;
        finished.Should().Equal("first", "second");
    });

    [Test]
    public Task Idle_with_nothing_running_completes_at_once() => UiThread.RunAsync(async () =>
    {
        var idle = _work.WhenIdleAsync();

        await idle.WaitAsync(TimeSpan.FromSeconds(5));
        _work.Running.Should().Be(0);
    });
}
