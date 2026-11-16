namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>What the Sub-Assemblies screen's Scale makes weigh what's typed: the whole batch, or the lines ticked as its base.</summary>
public enum BatchScaleBasis
{
    Batch,
    Base,
}

/// <summary>A choice in Scale's picker: the basis and how it reads, "the whole batch".</summary>
public sealed record BatchScaleOption(BatchScaleBasis Basis, string Label)
{
    public override string ToString() => Label;
}
