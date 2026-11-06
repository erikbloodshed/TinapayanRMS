using System.Reflection;
using System.Threading.Channels;

namespace TinapayanRMS.Tests;

/// <summary>
/// Lets scripts/Watch-Tests.ps1 rerun the tests by hot reload without a failure of TUnit's own. tests/Directory.Build.props
/// compiles this file into every test project.
/// <para>
/// Microsoft.Testing.Platform's hot reload runs the tests again in the same test session each time, and TUnit 1.81.0
/// keeps one DynamicTestQueue per session, which it completes at the end of every run. The second run's completion threw
/// "ChannelClosedException: The channel has been closed.", which TUnit reports as a failed test, so every run after the
/// first ended "Failed!". Before a test of such a run, this gives the queue a new channel, as a new session would have.
/// </para>
/// <para>
/// It reaches into TUnit's internals by name, so only under hot reload (TESTINGPLATFORM_HOTRELOAD_ENABLED, which only
/// Watch-Tests.ps1 sets): every other run leaves TUnit as it is. If a later TUnit renames what it reaches for, it does
/// nothing, and the failure shows again. Delete this file once TUnit's own reruns stop failing.
/// </para>
/// </summary>
public static class HotReloadReruns
{
    private static readonly bool UnderHotReload = Environment.GetEnvironmentVariable("TESTINGPLATFORM_HOTRELOAD_ENABLED") == "1";

    private static readonly PropertyInfo? Services =
        typeof(TestContext).GetProperty("ServiceProvider", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Type? QueueType = Type.GetType("TUnit.Engine.Interfaces.IDynamicTestQueue, TUnit.Engine");

    private static readonly Lock Gate = new();

    /// <summary>
    /// A new, open channel for the session's dynamic test queue when the run before this one completed it. The scheduler
    /// has stopped reading the old channel by now: it reads it once as the run starts, and a completed, empty channel ends
    /// that read at once.
    /// </summary>
    [BeforeEvery(Test)]
    public static void ReopenTheDynamicTestQueue(TestContext context)
    {
        if (!UnderHotReload || Services?.GetValue(context) is not IServiceProvider services || QueueType is null
            || services.GetService(QueueType) is not { } queue)
        {
            return;
        }

        var queueClass = queue.GetType();
        var channelField = queueClass.GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic);
        var completedField = queueClass.GetField("_isCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
        if (channelField is null || completedField is null)
        {
            return;
        }

        lock (Gate)
        {
            if (completedField.GetValue(queue) is not true)
            {
                return;
            }

            var itemType = channelField.FieldType.GetGenericArguments()[0];
            var channel = typeof(Channel)
                .GetMethod(nameof(Channel.CreateUnbounded), [typeof(UnboundedChannelOptions)])!
                .MakeGenericMethod(itemType)
                .Invoke(null, [new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }]);
            channelField.SetValue(queue, channel);
            completedField.SetValue(queue, false);
        }
    }
}
