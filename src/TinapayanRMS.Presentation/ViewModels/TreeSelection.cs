using System.Collections.ObjectModel;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The rows picked in a screen's list (Products, Raw Materials, Sub-Assemblies), shared with the tree as its SelectedItems:
/// one row, or several with Ctrl or Shift held. The tree changes them as rows are clicked, and the screen as the item it
/// shows changes (<see cref="BringFirst"/>). The screen still says what a change means (its TreeSelectionChanged): a row
/// picked by the user picks its item, unless <see cref="IsSyncing"/>.
/// </summary>
public sealed class TreeSelection
{
    /// <summary>The rows picked, bound to the tree's SelectedItems. The first is the screen's SelectedTreeNode.</summary>
    public ObservableCollection<object> Nodes { get; } = [];

    /// <summary>
    /// Set while the selection changes for a reason that's no pick of the user's: <see cref="BringFirst"/> moving it to the
    /// item shown, or the tree dropping a row from it as the row moves to another folder (<see cref="Quietly"/>).
    /// </summary>
    public bool IsSyncing { get; private set; }

    /// <summary>Picks <paramref name="row"/> alone, as a click does, or nothing for null.</summary>
    public void Pick(object? row)
    {
        if (row is null)
        {
            if (Nodes.Count > 0)
            {
                Nodes.Clear();
            }
        }
        else if (Nodes.Count != 1 || !ReferenceEquals(Nodes[0], row))
        {
            Nodes.Clear();
            Nodes.Add(row);
        }
    }

    /// <summary>
    /// Puts <paramref name="row"/>, the row of the item the screen shows, first among the rows picked: alone, unless it's
    /// picked already, when the rows picked with it stay. Null (nothing shown, or a search hiding its row) picks no row.
    /// </summary>
    public void BringFirst(object? row)
    {
        var at = row is null ? -1 : Nodes.IndexOf(row);
        if (at == 0)
        {
            return;
        }

        Quietly(() =>
        {
            if (at > 0)
            {
                Nodes.Move(at, 0);
            }
            else
            {
                Pick(row);
            }
        });
    }

    /// <summary>Picks again the rows of <paramref name="picked"/> the tree dropped from its selection as they moved, those still listed.</summary>
    /// <param name="isListed">Whether a row is still in the tree: a folder's always is, an item's if the tree's row for it is that one.</param>
    public void Restore(IReadOnlyList<object> picked, Func<object, bool> isListed) =>
        Quietly(() =>
        {
            foreach (var row in picked.Where(isListed))
            {
                if (!Nodes.Contains(row))
                {
                    Nodes.Add(row);
                }
            }
        });

    /// <summary>Makes <paramref name="change"/> with <see cref="IsSyncing"/> set, so nothing it does to the selection reads as the user's pick.</summary>
    public void Quietly(Action change)
    {
        var wasSyncing = IsSyncing;
        IsSyncing = true;
        try
        {
            change();
        }
        finally
        {
            IsSyncing = wasSyncing;
        }
    }
}
