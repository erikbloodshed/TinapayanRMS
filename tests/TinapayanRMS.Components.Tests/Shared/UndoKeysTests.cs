using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// UndoKeys registers Ctrl+Z and Ctrl+Y with the document. A screen left before the page had registered them (a quick
/// Back, the Vim keys' gt) used to keep them registered for the life of the page: every later Ctrl+Z took the cursor out of
/// its field and called a reference already let go of.
/// </summary>
public class UndoKeysTests
{
    private sealed class Screen : IUndoScreen
    {
        public bool IsEditPending { get; set; }

        public int FormVersion => 0;

        public void BeginUndoStep()
        {
        }

        public void EndUndoStep()
        {
        }

        public bool TryUndo() => false;

        public bool TryRedo() => false;
    }

    // BL0016 asks for every JS interop call to be inside a try: these are the page, standing in for app.js, not calls to it.
#pragma warning disable BL0016

    /// <summary>What app.js hands back for a registration: an object whose dispose() undoes it.</summary>
    private sealed class Registration : IJSObjectReference
    {
        private readonly TaskCompletionSource _letGo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the reference has been let go of, after whatever was called on it.</summary>
        public Task LetGo => _letGo.Task;

        /// <summary>What was called on it, in order.</summary>
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Record<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => Record<TValue>(identifier);

        public ValueTask DisposeAsync()
        {
            _letGo.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private ValueTask<TValue> Record<TValue>(string identifier)
        {
            Calls.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    /// <summary>The page, slow to answer the undo keys' registration: it answers when the test says.</summary>
    private sealed class SlowPage : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Registering { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Answer<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => Answer<TValue>(identifier);

        private async ValueTask<TValue> Answer<TValue>(string identifier) =>
            identifier == "tinapayan.registerUndoKeys" ? (TValue)await Registering.Task : default!;
    }

#pragma warning restore BL0016

    [Test]
    public async Task Keys_registered_after_their_screen_was_left_are_unregistered_at_once()
    {
        await using var context = AppServices.NewContext();
        var page = new SlowPage();
        context.Services.AddSingleton<IJSRuntime>(page);

        context.ShowAndLeave<UndoKeys>(keys => keys
            .Add(k => k.Screen, new Screen())
            .Add(k => k.Redraw, () => Task.CompletedTask));
        var registration = new Registration();
        page.Registering.SetResult(registration);

        await registration.LetGo.WaitAsync(TimeSpan.FromSeconds(10));
        registration.Calls.Should().Equal(["dispose"], "the page's keys are unregistered, not only the reference let go of");
    }
}
