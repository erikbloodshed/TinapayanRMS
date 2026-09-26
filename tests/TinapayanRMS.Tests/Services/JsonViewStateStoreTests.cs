using System.IO;
using AwesomeAssertions;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Tests.Services;

/// <summary>What the app remembers of how its screens were left, one file per Windows user: the Products list's open folders.</summary>
public sealed class JsonViewStateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TinapayanRMS.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "view-state.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_list_written_reads_back_the_next_time_the_app_starts()
    {
        new JsonViewStateStore(FilePath).WriteList("Products.OpenFolders", ["Production", "Production/12"]);

        new JsonViewStateStore(FilePath).ReadList("Products.OpenFolders").Should().Equal("Production", "Production/12");
    }

    [Fact]
    public void Writing_one_list_keeps_the_others()
    {
        var store = new JsonViewStateStore(FilePath);
        store.WriteList("A", ["1"]);
        store.WriteList("B", ["2"]);
        store.WriteList("A", ["3"]);

        var next = new JsonViewStateStore(FilePath);
        next.ReadList("A").Should().Equal("3");
        next.ReadList("B").Should().Equal("2");
    }

    [Fact]
    public void Nothing_written_or_a_file_that_cant_be_read_reads_as_nothing()
    {
        new JsonViewStateStore(FilePath).ReadList("Products.OpenFolders").Should().BeNull("there's no file yet");

        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");
        var store = new JsonViewStateStore(FilePath);
        store.ReadList("Products.OpenFolders").Should().BeNull("a broken file is only a convenience lost");

        store.WriteList("Products.OpenFolders", ["Production"]);
        new JsonViewStateStore(FilePath).ReadList("Products.OpenFolders").Should().Equal("Production");
    }
}
