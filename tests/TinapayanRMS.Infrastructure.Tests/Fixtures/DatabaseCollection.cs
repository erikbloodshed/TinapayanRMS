using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.Infrastructure.Tests.Fixtures;

/// <summary>
/// The four collections a database test class goes in, each with a database of its own (DatabaseFixtureBase), so they
/// run side by side while the tests in one run one at a time (2026-11-05). With one collection, this project's database
/// tests took 352 s one after another. They're balanced by the time their classes take: DatabaseBackupsTests, the
/// longest, has A to itself. Put a new class in whichever of B to D has least in it; a class's own tests always run
/// together.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Named for the collections it names, as their definitions are.")]
public static class DatabaseCollection
{
    public const string A = "Database A";
    public const string B = "Database B";
    public const string C = "Database C";
    public const string D = "Database D";
}

[CollectionDefinition(DatabaseCollection.A)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class DatabaseCollectionA : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition(DatabaseCollection.B)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class DatabaseCollectionB : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition(DatabaseCollection.C)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class DatabaseCollectionC : ICollectionFixture<DatabaseFixture>;

[CollectionDefinition(DatabaseCollection.D)]
[SuppressMessage("Naming", "CA1711", Justification = "xUnit names a collection definition class after the collection it defines.")]
public class DatabaseCollectionD : ICollectionFixture<DatabaseFixture>;
