using System.Collections.ObjectModel;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Brings a folder tree's rows in line with what should show, moving the rows it has rather than replacing them: a row
/// keeps its object, which the tree's selection and open folders are tied to. ProductTree, RawMaterialTree and
/// SubAssemblyTree lay out each level of folders with it: <see cref="RemoveLeaving"/> for every level first, then
/// <see cref="LayFolders"/> or <see cref="LayTopFolder"/>.
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

    /// <summary>
    /// The first half of laying out a level of folders: takes out of <paramref name="parent"/> the folders that won't show
    /// (while a search filters the list, <paramref name="isFiltered"/>, the ones with no rows), and out of each folder the
    /// rows no longer in it. Run for every level before laying any out, so a row moving from one folder to another is never
    /// in both at once.
    /// </summary>
    internal static void RemoveLeaving<TRow, TFolder, TNode>(
        ObservableCollection<TRow> parent, IReadOnlyList<(TFolder Folder, List<TNode> Rows)> wanted, bool isFiltered)
        where TRow : class
        where TFolder : class, TRow, ITreeFolder<TNode>
        where TNode : class
    {
        RemoveMissing(parent, Shown<TRow, TFolder, TNode>(wanted, isFiltered));
        foreach (var (folder, rows) in wanted)
        {
            RemoveMissing(folder.Children, rows);
        }
    }

    /// <summary>
    /// The second half: puts the folders that show in <paramref name="parent"/>, and each folder's rows in it, in order,
    /// and has each folder count its rows. While a search filters the list, every folder shown opens, so every match
    /// shows; they stay open once it's cleared, as in a file explorer. An open folder just shown, or just given its first
    /// rows, is announced (ITreeFolder.AnnounceExpanded).
    /// </summary>
    internal static void LayFolders<TRow, TFolder, TNode>(
        ObservableCollection<TRow> parent, IReadOnlyList<(TFolder Folder, List<TNode> Rows)> wanted, bool isFiltered)
        where TRow : class
        where TFolder : class, TRow, ITreeFolder<TNode>
        where TNode : class
    {
        foreach (var folder in Lay(parent, wanted, isFiltered).ToAnnounce)
        {
            folder.AnnounceExpanded();
        }
    }

    /// <summary>
    /// <see cref="LayFolders"/> for a top-level folder whose rows are folders (a stage, a type), which counts the rows in
    /// all of them and opens on a search's matches. It's announced open, before the folders in it, once it has rows: the
    /// screen showing at startup binds its tree before the first load has filled it, and the tree holds an empty folder
    /// closed however it's set.
    /// </summary>
    internal static void LayTopFolder<TFolder, TNode>(TFolder top, IReadOnlyList<(TFolder Folder, List<TNode> Rows)> wanted, bool isFiltered)
        where TFolder : class, TNode, ITreeFolder<TNode>
        where TNode : class
    {
        var wasEmpty = top.Children.Count == 0;
        var (count, toAnnounce) = Lay(top.Children, wanted, isFiltered);
        top.ShowCount(count);
        if (isFiltered)
        {
            top.IsExpanded = true;
        }

        if (wasEmpty && top.Children.Count > 0 && top.IsExpanded)
        {
            top.AnnounceExpanded();
        }

        foreach (var folder in toAnnounce)
        {
            folder.AnnounceExpanded();
        }
    }

    /// <summary>LayFolders but for the announcing: how many rows the folders hold, and the folders to announce.</summary>
    private static (int Count, List<TFolder> ToAnnounce) Lay<TRow, TFolder, TNode>(
        ObservableCollection<TRow> parent, IReadOnlyList<(TFolder Folder, List<TNode> Rows)> wanted, bool isFiltered)
        where TRow : class
        where TFolder : class, TRow, ITreeFolder<TNode>
        where TNode : class
    {
        var shown = Shown<TRow, TFolder, TNode>(wanted, isFiltered);
        var added = Arrange(parent, shown);
        List<TFolder> filled = [];
        foreach (var (folder, rows) in wanted)
        {
            if (folder.Children.Count == 0 && rows.Count > 0)
            {
                filled.Add(folder);
            }

            Arrange(folder.Children, rows);
            folder.ShowCount(rows.Count);
        }

        if (isFiltered)
        {
            foreach (var folder in shown.Cast<TFolder>())
            {
                folder.IsExpanded = true;
            }
        }

        return (wanted.Sum(f => f.Rows.Count), [.. added.Cast<TFolder>().Union(filled).Where(f => f.IsExpanded)]);
    }

    /// <summary>The folders of <paramref name="wanted"/> that show: all of them, or while a search filters the list, those holding a match.</summary>
    private static List<TRow> Shown<TRow, TFolder, TNode>(IReadOnlyList<(TFolder Folder, List<TNode> Rows)> wanted, bool isFiltered)
        where TFolder : TRow =>
        [.. wanted.Where(f => !isFiltered || f.Rows.Count > 0).Select(f => (TRow)f.Folder)];
}
