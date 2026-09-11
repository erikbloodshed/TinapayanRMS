namespace TinapayanRMS.Domain;

/// <summary>
/// The four length units are seeded with these fixed ids and can never be edited
/// or deleted, because TinapayanRMS.ApplicationCore's LengthConversion helper
/// hardcodes conversion factors keyed by these exact ids.
/// </summary>
public static class WellKnownUnitOfMeasureIds
{
    public const int Millimeter = 1;
    public const int Centimeter = 2;
    public const int Inch = 3;
    public const int Meter = 4;
    public const int Gram = 5;
    public const int Piece = 6;

    public static readonly IReadOnlyCollection<int> ProtectedLengthUnitIds =
        [Millimeter, Centimeter, Inch, Meter];
}
