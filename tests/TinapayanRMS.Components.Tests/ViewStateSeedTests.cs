using AwesomeAssertions;

namespace TinapayanRMS.Components.Tests;

/// <summary>The MAUI app's view state starts as a copy of the WPF app's, once, and is its own from then on.</summary>
public sealed class ViewStateSeedTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tinapayanrms-view-state-seed", Guid.NewGuid().ToString("N"));

    private string WpfFile => Path.Combine(_folder, "view-state.json");

    private string MauiFile => Path.Combine(_folder, "Maui", "view-state.json");

    [Test]
    public void The_first_run_starts_from_the_WPF_apps_file()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(WpfFile, """{"vim-keys":["on"]}""");

        ViewStateSeed.StartFrom(WpfFile, MauiFile);

        File.ReadAllText(MauiFile).Should().Be("""{"vim-keys":["on"]}""");
    }

    [Test]
    public void A_file_of_its_own_is_never_replaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MauiFile)!);
        File.WriteAllText(WpfFile, """{"vim-keys":["on"]}""");
        File.WriteAllText(MauiFile, """{"vim-keys":[]}""");

        ViewStateSeed.StartFrom(WpfFile, MauiFile);

        File.ReadAllText(MauiFile).Should().Be("""{"vim-keys":[]}""", "the MAUI app's own changes since its first run are kept");
    }

    [Test]
    public void With_no_WPF_file_it_starts_empty()
    {
        ViewStateSeed.StartFrom(WpfFile, MauiFile);

        File.Exists(MauiFile).Should().BeFalse("JsonViewStateStore reads a missing file as empty");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
