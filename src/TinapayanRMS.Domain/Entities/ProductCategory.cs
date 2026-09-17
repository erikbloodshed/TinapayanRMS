namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for products (Breads, Cakes, Pastries, …), a list of its own apart from raw material categories.</summary>
public class ProductCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
