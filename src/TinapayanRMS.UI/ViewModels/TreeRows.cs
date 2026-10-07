using System.Collections.ObjectModel;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Brings a folder tree's rows in line with what should show, moving the rows it has rather than replacing them: a row
/// keeps its object, which the tree's selection and open folders are tied to. ProductTree, RawMaterialTree and
/// SubAssemblyTree use it.
/// </summary>
public static class TreeRows
{
    /// <summary>Removes every row of <paramref name="list"/> that isn't in <paramref name="wanted"/>.</summary>
    public static void RemoveMissing<T>(ObservableCollection<T> list, IReadOnlyCollection<T> wanted)
        where T : class
    {
        var keep = wanted.ToHashSet();
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(list[i]))
            {
                list.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Puts <paramref name="list"/> in <paramref name="wanted"/>'s order, moving the rows it has and adding the ones it
    /// hasn't. Returns the ones it added.
    /// </summary>
    public static List<T> Arrange<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)
        where T : class
    {
        List<T> added = [];
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && ReferenceEquals(list[i], wanted[i]))
            {
                continue;
            }

            var at = list.IndexOf(wanted[i]);
            if (at >= 0)
            {
                list.Move(at, i);
            }
            else
            {
                list.Insert(i, wanted[i]);
                added.Add(wanted[i]);
            }
        }

        return added;
    }
}
