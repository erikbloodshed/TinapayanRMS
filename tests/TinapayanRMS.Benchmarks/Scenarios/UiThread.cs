using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// A thread that runs one thing at a time, in the order it was posted, as the app's UI thread runs its screens: every
/// await in a screen state comes back to it, as in the window (Presentation.Tests' UiThread, kept running here for a whole
/// run rather than made for each test). The work it's given runs in the caller's execution context, so what flows with
/// it (a SqlCommandCounter) flows into the screen's work too.
/// </summary>
public sealed class UiThread : IDisposable
{
    private readonly OneAtATimeContext _context = new();
    private readonly Thread _thread;

    public UiThread()
    {
        _thread = new Thread(_context.Run) { IsBackground = true, Name = "Benchmark UI thread" };
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the thread; fails as it fails, or as work it posted there failed meanwhile.</summary>
    public Task RunAsync(Func<Task> work) => RunAsync(async () =>
    {
        await work();
        return true;
    });

    /// <inheritdoc cref="RunAsync(Func{Task})"/>
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = ExecutionContext.Capture();
        _context.Post(_ =>
        {
            if (flow is null)
            {
                Start(work, done);
            }
            else
            {
                ExecutionContext.Run(flow, _ => Start(work, done), null);
            }
        }, null);
        return done.Task;
    }

    public void Dispose()
    {
        _context.Complete();
        _thread.Join();
        _context.Dispose();
    }

    private async void Start<T>(Func<Task<T>> work, TaskCompletionSource<T> done)
    {
        try
        {
            var result = await work();
            _context.ThrowAnyFailure();
            done.SetResult(result);
        }
        catch (Exception ex)
        {
            done.SetException(ex);
        }
    }

    /// <summary>The queue the thread works through.</summary>
    private sealed class OneAtATimeContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        /// <summary>What an async void handler threw, which would end the app: the next run fails on it.</summary>
        private ExceptionDispatchInfo? _failure;

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The run has ended.
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

        public void ThrowAnyFailure() => Interlocked.Exchange(ref _failure, null)?.Throw();

        public void Complete() => _queue.CompleteAdding();

        public void Run()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    callback(state);
                }
                catch (Exception ex)
                {
                    _failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        }

        public void Dispose() => _queue.Dispose();
    }
}
