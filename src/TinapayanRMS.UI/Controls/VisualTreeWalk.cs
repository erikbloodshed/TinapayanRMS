using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Walking up from an element to what contains it. A cell's dropdown and the description editor open in popups,
/// which are outside the visual tree of what they belong to, so a plain parent walk stops at the popup's own root
/// and the element looks as though it's in no grid and on no screen. Following a popup's PlacementTarget keeps the
/// walk on the path the user sees. Also walking down, through the logical tree, to reach what isn't on show.
/// </summary>
internal static class VisualTreeWalk
{
    /// <summary>
    /// <paramref name="element"/> and everything above it, stepping out of popups as it goes. It may start on text
    /// content (a Hyperlink takes the keyboard when it's clicked), which isn't in the visual tree.
    /// </summary>
    public static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject? element)
    {
        for (var current = element; current is not null; current = ParentOf(current))
        {
            yield return current;

            if (current is Popup { PlacementTarget: { } target })
            {
                current = target;
                yield return current;
            }
        }
    }

    /// <summary>
    /// What contains <paramref name="element"/>: its visual parent, or its logical one where it has none. Text content
    /// (a ContentElement, such as a Hyperlink or a Run) has no visual parent, and VisualTreeHelper throws on it, so it
    /// steps to the element that holds it: its logical parent, or the text block or document hosting it.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject element) => element switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element),
        ContentElement content => LogicalTreeHelper.GetParent(content) ?? ContentOperations.GetParent(content),
        _ => LogicalTreeHelper.GetParent(element),
    };

    /// <summary>Whether <paramref name="element"/> is inside <paramref name="container"/>.</summary>
    public static bool IsWithin(DependencyObject container, DependencyObject? element) =>
        SelfAndAncestors(element).Any(candidate => ReferenceEquals(candidate, container));

    /// <summary>The nearest <typeparamref name="T"/> at or above <paramref name="element"/>, or null.</summary>
    public static T? FindAncestor<T>(DependencyObject? element)
        where T : class =>
        SelfAndAncestors(element).OfType<T>().FirstOrDefault();

    /// <summary>
    /// <paramref name="element"/> and everything in its logical tree. Unlike the visual tree, that takes in the tabs
    /// not showing, since a tab keeps its content as a logical child while another one's is on show.
    /// </summary>
    public static IEnumerable<DependencyObject> SelfAndLogicalDescendants(DependencyObject element)
    {
        yield return element;

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            foreach (var descendant in SelfAndLogicalDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
