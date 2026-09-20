using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Walking up from an element to what contains it. A cell's dropdown and the description editor open in popups,
/// which are outside the visual tree of what they belong to, so a plain parent walk stops at the popup's own root
/// and the element looks as though it's in no grid and on no screen. Following a popup's PlacementTarget keeps the
/// walk on the path the user sees.
/// </summary>
internal static class VisualTreeWalk
{
    /// <summary><paramref name="element"/> and everything above it, stepping out of popups as it goes.</summary>
    public static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject? element)
    {
        for (var current = element;
             current is not null;
             current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
        {
            yield return current;

            if (current is Popup { PlacementTarget: { } target })
            {
                current = target;
                yield return current;
            }
        }
    }

    /// <summary>Whether <paramref name="element"/> is inside <paramref name="container"/>.</summary>
    public static bool IsWithin(DependencyObject container, DependencyObject? element) =>
        SelfAndAncestors(element).Any(candidate => ReferenceEquals(candidate, container));

    /// <summary>The nearest <typeparamref name="T"/> at or above <paramref name="element"/>, or null.</summary>
    public static T? FindAncestor<T>(DependencyObject? element)
        where T : class =>
        SelfAndAncestors(element).OfType<T>().FirstOrDefault();
}
