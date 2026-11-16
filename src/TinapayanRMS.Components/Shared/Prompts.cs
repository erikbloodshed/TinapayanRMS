using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A name asked for: a new category's, or a record's new name from a list's
/// menu (a screen state's PromptForName or PromptForNewCategory, answered by <see cref="PromptHost"/>'s dialog); or a name and
/// a pick from a list, as Move to In-House and Make In-House Ingredient ask a made ingredient's name and Made category
/// (PromptForMadeIngredient). One per page the window loads. The answer is what was typed (and picked), or null when it's cancelled;
/// asking again while one is open cancels it.
/// </summary>
public sealed class Prompts
{
    private TaskCompletionSource<ChoicePromptResult?>? _pending;

    /// <summary>What's being asked, or null when nothing is.</summary>
    public TextPromptRequest? Request { get; private set; }

    /// <summary>The list to pick from with the name, for a choice prompt; null for a name alone.</summary>
    public ChoicePromptRequest? Choice { get; private set; }

    public event Action? Changed;

    public async Task<string?> AskAsync(TextPromptRequest request) => (await OpenAsync(request, null))?.Text;

    /// <summary>A name and a pick from <paramref name="request"/>'s Choices; null when it's cancelled.</summary>
    public Task<ChoicePromptResult?> AskChoiceAsync(ChoicePromptRequest request) =>
        OpenAsync(new TextPromptRequest(request.Title, request.Label, request.InitialValue, request.Message), request);

    private Task<ChoicePromptResult?> OpenAsync(TextPromptRequest request, ChoicePromptRequest? choice)
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<ChoicePromptResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Request = request;
        Choice = choice;
        Changed?.Invoke();
        return _pending.Task;
    }

    /// <summary>Closes the prompt with <paramref name="text"/> and the choice picked, or with null for Cancel.</summary>
    public void Answer(string? text, int choice = 0)
    {
        var pending = _pending;
        _pending = null;
        Request = null;
        Choice = null;
        Changed?.Invoke();
        pending?.TrySetResult(text is null ? null : new ChoicePromptResult(text, choice));
    }
}
