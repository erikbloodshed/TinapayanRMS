using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.Tests.Fixtures;

/// <summary>
/// The grid behaviour tests that show a real grid in an off-screen window (<see cref="OffscreenGrid"/>). They run one at a
/// time: each class was a collection of its own, so their windows were up at once, on separate STA threads, and a
/// different current-cell or focus test failed on almost every full run while each passed alone.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class OffscreenGridCollection
{
    public const string Name = "Offscreen grid";
}
