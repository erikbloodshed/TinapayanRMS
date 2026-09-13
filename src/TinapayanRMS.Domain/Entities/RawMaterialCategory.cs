namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for raw materials (Flour, Dairy, Boxes, …), one list shared by ingredients and packaging.</summary>
public class RawMaterialCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
