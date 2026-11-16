namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// Where the sub-assembly being added comes from, picked on Add Sub-Assembly's drop-down
/// (RecipeStructure) and carried by AddUsageCommand. New is the default, which is what a
/// parameterless Execute() passes.
/// </summary>
public enum UsageFormMode
{
    /// <summary>A new sub-assembly, kept on the Sub-Assemblies screen once saved, and linked.</summary>
    New,

    /// <summary>An existing sub-assembly, used as it is and following its changes.</summary>
    Link,

    /// <summary>A new sub-assembly starting from an existing one's role and lines; nothing connects the two afterward.</summary>
    Copy,
}
