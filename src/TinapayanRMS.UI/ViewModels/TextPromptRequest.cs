namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What a screen asks a TextPromptWindow to show: its heading, its box's label, the text the box starts with (selected,
/// so typing replaces it), and an optional line under the heading saying more.
/// </summary>
public sealed record TextPromptRequest(string Title, string Label, string InitialValue, string? Message = null);
