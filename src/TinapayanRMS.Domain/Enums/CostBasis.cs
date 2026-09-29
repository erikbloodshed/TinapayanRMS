namespace TinapayanRMS.Domain.Enums;

/// <summary>
/// Which of a raw material's two costs per base unit a product is priced from: the buffered one (BufferedCostPerBaseUnit),
/// as it always is, or the true one (CostPerBaseUnit), which the Products screen can switch to for a look without the buffer.
/// </summary>
public enum CostBasis
{
    Buffered,
    Unbuffered,
}
