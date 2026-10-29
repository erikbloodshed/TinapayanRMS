namespace TinapayanRMS.Domain.Entities;

public sealed class UnitOfMeasure
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;
}
