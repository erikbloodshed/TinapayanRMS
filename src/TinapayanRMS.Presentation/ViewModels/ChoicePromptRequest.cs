namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// What a prompt with a name box and a picker under it shows (TextPromptWindow.PromptWithChoice): its title, the box's
/// label and starting text, the picker's label, its choices and the one it starts on, and an optional line saying more.
/// </summary>
public sealed record ChoicePromptRequest(
    string Title,
    string Label,
    string InitialValue,
    string ChoiceLabel,
    IReadOnlyList<string> Choices,
    int InitialChoice,
    string? Message = null);

/// <summary>What was typed into a ChoicePromptRequest's box, never empty, and the index of the choice picked.</summary>
public sealed record ChoicePromptResult(string Text, int Choice);
