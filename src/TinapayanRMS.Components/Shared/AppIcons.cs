using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Serialization;
using Syncfusion.Blazor.Buttons;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// The Syncfusion icon each of the app's icons is drawn with (the theme's own icon font, so they look the same on any
/// system). <see cref="Icon"/> draws one with Syncfusion's SfIcon; a row drawn hundreds of times (a tree's) puts
/// <see cref="CssOf"/> on a span instead, which is what SfIcon draws, without a component per row.
/// </summary>
public static class AppIcons
{
    public static IconName NameOf(AppIcon icon) => icon switch
    {
        AppIcon.Add => IconName.Plus,
        AppIcon.Save => IconName.Save,
        AppIcon.Undo => IconName.Undo,
        AppIcon.Redo => IconName.Redo,
        AppIcon.Apply => IconName.Check,
        AppIcon.Delete => IconName.Trash,
        AppIcon.Remove => IconName.CircleRemove,
        AppIcon.Cancel => IconName.Close,
        AppIcon.Edit => IconName.Edit,
        AppIcon.Rename => IconName.Rename,
        AppIcon.Calculate => IconName.Calculation,
        AppIcon.Copy => IconName.Copy,
        AppIcon.Link => IconName.Link,
        AppIcon.Unlink => IconName.LinkRemove,
        AppIcon.MoveUp => IconName.ArrowUp,
        AppIcon.MoveDown => IconName.ArrowDown,
        AppIcon.Move => IconName.Move,
        AppIcon.Settings => IconName.Settings,
        AppIcon.Columns => IconName.Columns,
        AppIcon.Print => IconName.Print,
        AppIcon.Restore => IconName.History,
        AppIcon.Folder => IconName.Folder,
        AppIcon.FolderOpen => IconName.FolderOpen,
        AppIcon.NewFolder => IconName.Folder,
        AppIcon.ExportToExcel => IconName.ExportExcel,
        AppIcon.ExportToPdf => IconName.ExportPdf,
        AppIcon.InHouse => IconName.Home,
        AppIcon.Composite => IconName.SendToBack,
        AppIcon.RawMaterials => IconName.Grain,
        AppIcon.SubAssemblies => IconName.Layers,
        AppIcon.Products => IconName.GridView,
        AppIcon.Reports => IconName.Chart,
        AppIcon.PriceSimulator => IconName.CalculatedMember,
        AppIcon.Section => IconName.ListUnordered,
        AppIcon.Packaging => IconName.Box,
        AppIcon.Expanded => IconName.ChevronDown,
        AppIcon.Collapsed => IconName.ChevronRight,
        AppIcon.ExpandAll => IconName.ChevronDownDouble,
        AppIcon.CollapseAll => IconName.ChevronUpDouble,
        AppIcon.CurrentRow => IconName.ChevronRight,
        AppIcon.DragHandle => IconName.GripVertical,
        AppIcon.ConvertsTo => IconName.ArrowRight,
        AppIcon.Search => IconName.Search,
        AppIcon.Warning => IconName.Warning,
        AppIcon.Back => IconName.ArrowLeft,
        AppIcon.Forward => IconName.ArrowRight,
        AppIcon.Download => IconName.Download,
        AppIcon.Upload => IconName.Upload,
        AppIcon.Lock => IconName.Lock,
        AppIcon.Cut => IconName.Cut,
        AppIcon.Paste => IconName.Paste,
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
    };

    // Each icon's classes, worked out once: SfIcon reads the icon's CSS name off IconName's EnumMember attribute.
    private static readonly FrozenDictionary<AppIcon, string> Classes = Enum.GetValues<AppIcon>().ToFrozenDictionary(
        icon => icon,
        icon => "glyph e-icons e-" + typeof(IconName).GetField(NameOf(icon).ToString())!.GetCustomAttribute<EnumMemberAttribute>()!.Value);

    /// <summary>The classes that draw <paramref name="icon"/> on a span: the app's glyph, and Syncfusion's icon.</summary>
    public static string CssOf(AppIcon icon) => Classes[icon];
}
