namespace TinapayanRMS.Presentation.Trees;

/// <summary>
/// A row of a screen's list whose name is being typed in place, as in a file explorer: a record or folder renamed, or a
/// new one named. The list draws an edit box on <paramref name="Node"/>'s row holding <paramref name="InitialText"/>,
/// selected, and gives what's typed back to the screen when Enter is pressed or the box is left, or nothing on Esc.
/// </summary>
/// <param name="Node">The row being named.</param>
/// <param name="InitialText">What the box starts with: the name it has, or what a new one is called until it's typed.</param>
/// <param name="Hint">The box's tooltip, saying what the name is for, or null for none.</param>
public sealed record TreeNameEdit(object Node, string InitialText, string? Hint = null);
