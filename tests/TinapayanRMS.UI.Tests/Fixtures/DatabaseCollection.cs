using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.UI.Tests.Fixtures;

[CollectionDefinition(Name)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
