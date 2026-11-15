namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The app's icons, by what each stands for. A ViewModel names one (a menu item's, a tree row's, a tab's); the screens
/// draw it from Syncfusion's own icons (TinapayanRMS.Components' AppIcons, which picks the icon for each), so an action
/// shows the same icon wherever it's offered.
/// </summary>
public enum AppIcon
{
    Add,
    Save,
    Undo,
    Redo,
    Apply,
    Delete,

    /// <summary>Removes a line from a table, as against Delete, which deletes a record.</summary>
    Remove,
    Cancel,
    Edit,
    Rename,
    Calculate,
    Copy,
    Link,
    Unlink,
    MoveUp,
    MoveDown,

    /// <summary>Moves records to another folder.</summary>
    Move,
    Settings,
    Columns,
    Print,

    /// <summary>Goes back to an earlier version, or to a backup.</summary>
    Restore,
    Folder,
    FolderOpen,
    NewFolder,
    ExportToExcel,

    /// <summary>Made in-house: a made ingredient, or a section moved to one.</summary>
    InHouse,

    /// <summary>A composite product, made of other products.</summary>
    Composite,

    // The screens, as the navigation menu shows them.
    RawMaterials,
    SubAssemblies,
    Products,
    Reports,
    PriceSimulator,

    /// <summary>A product's sub-assembly section, typed in on its own tab.</summary>
    Section,
    Packaging,

    /// <summary>A folder open on its rows, as its expander shows it.</summary>
    Expanded,

    /// <summary>A folder closed, as its expander shows it.</summary>
    Collapsed,
    ExpandAll,
    CollapseAll,

    /// <summary>The row being edited, as a table's row header marks it.</summary>
    CurrentRow,

    /// <summary>Where a row is dragged by to move it.</summary>
    DragHandle,

    /// <summary>Between a unit and what it's worth in another (1 sack → 25 kg).</summary>
    ConvertsTo,
    Search,
    Warning,
    Back,
    Forward,
    Download,
    Upload,
    Menu,

    /// <summary>The title bar's Lock, which puts the sign-in panel over the window (AppLock).</summary>
    Lock,

    // The Products list's Cut and Paste, which move products and folders as a drag does.
    Cut,
    Paste,
}
