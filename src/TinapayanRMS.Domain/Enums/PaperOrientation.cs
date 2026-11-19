namespace TinapayanRMS.Domain.Enums;

/// <summary>Which way up a printout's paper is (Settings → Printing).</summary>
public enum PaperOrientation
{
    /// <summary>Taller than wide.</summary>
    Portrait,

    /// <summary>Wider than tall: the paper turned on its side.</summary>
    Landscape,
}
