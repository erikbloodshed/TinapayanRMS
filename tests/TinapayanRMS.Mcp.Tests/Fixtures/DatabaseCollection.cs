using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.Mcp.Tests.Fixtures;

/// <summary>
/// The one collection this project's database test classes go in, with the project's database (DatabaseFixtureBase). A
/// class names it twice, as the key of both <c>[NotInParallel(DatabaseCollection.Name)]</c>, which runs its tests one at
/// a time, and <c>[ClassDataSource&lt;DatabaseFixture&gt;(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]</c>,
/// which hands them the one fixture TUnit makes for it.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Named for the collection it names.")]
public static class DatabaseCollection
{
    public const string Name = "Database";
}
