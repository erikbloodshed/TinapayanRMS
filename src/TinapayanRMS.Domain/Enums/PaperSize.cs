namespace TinapayanRMS.Domain.Enums;

/// <summary>The paper a printout is laid out for (Settings → Printing). Its size is PrintRules.PaperSizeOf's.</summary>
public enum PaperSize
{
    /// <summary>210 × 297 mm.</summary>
    A4,

    /// <summary>8.5 × 11 in, short bond.</summary>
    Letter,

    /// <summary>8.5 × 13 in, long bond: what most of the Philippines calls long.</summary>
    LongBond,

    /// <summary>8.5 × 14 in.</summary>
    Legal,
}
