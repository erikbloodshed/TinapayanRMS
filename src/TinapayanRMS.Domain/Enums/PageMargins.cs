namespace TinapayanRMS.Domain.Enums;

/// <summary>How much blank paper is left around a printout's page (Settings → Printing), in PrintRules.MarginsOf.</summary>
public enum PageMargins
{
    /// <summary>14 mm, 16 mm at the foot, under the page number.</summary>
    Normal,

    /// <summary>8 mm, 12 mm at the foot: more on each page.</summary>
    Narrow,

    /// <summary>20 mm, 22 mm at the foot: room to write notes in.</summary>
    Wide,
}
