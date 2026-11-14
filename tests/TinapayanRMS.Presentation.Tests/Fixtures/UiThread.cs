using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>
/// Runs a test on a thread of its own with a synchronization context that runs one thing at a time, in the order it was
/// posted, as the app's UI thread runs its screens: the window's, which its web view's Blazor renderer runs on. Every
/// await in the test, a ViewModel's own included, comes back to that thread. A ViewModel only ever runs on the UI
/// thread: without this, a background read's await came back on the thread pool and changed the screen's state while
/// the test was changing it too (ProductsViewModelCompositeTests, where a lost update left a line's cost unread).
/// </summary>
public static class UiThread
{
    /// <summary>Runs <paramref name="test"/> on the thread. An exception it throws, or one thrown by work it posted, fails it.</summary>
    public static Task RunAsync(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new OneAtATimeContext(done);
            SynchronizationContext.SetSynchronizationContext(context);
            context.Post(
                async _ =>
                {
                    try
                    {
                        await test();
                        done.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                    finally
                    {
                        context.Complete();
                    }
                },
                null);
            context.Run();
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>
    /// The queue the thread works through. Work posted once the test has ended is dropped, as a closed window's would be.
    /// </summary>
    private sealed class OneAtATimeContext(TaskCompletionSource done) : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The test has ended.
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Current == this)
            {
                d(state);
                return;
            }

            using var sent = new ManualResetEventSlim();
            ExceptionDispatchInfo? failure = null;
            Post(
                s =>
                {
                    try
                    {
                        d(s);
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        sent.Set();
                    }
                },
                state);
            sent.Wait();
            failure?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Complete() => _queue.CompleteAdding();

        public void Run()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    callback(state);
                }
                catch (Exception ex)
                {
                    // What an async void handler threw: the app would end on it, so the test fails.
                    done.TrySetException(ex);
                }
            }
        }

        public void Dispose() => _queue.Dispose();
    }
}
