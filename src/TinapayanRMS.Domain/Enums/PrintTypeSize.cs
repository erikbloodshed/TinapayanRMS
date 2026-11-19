namespace TinapayanRMS.Domain.Enums;

/// <summary>How big a printout's type is (Settings → Printing), as a share of the normal size in PrintRules.TypeScaleOf.</summary>
public enum PrintTypeSize
{
    /// <summary>10.5 pt body text: the size printouts had before it could be chosen.</summary>
    Normal,

    /// <summary>A tenth smaller: more rows on each page.</summary>
    Compact,

    /// <summary>A fifth larger: easier to read at arm's length, in the kitchen.</summary>
    Large,
}
