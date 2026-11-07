using System.ComponentModel;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// Which of a list's folders start open, on its own, with folders of a test's own. The trees' use of it is in their
/// *TreeTests, and the screens' in their *ViewModelTreeTests.
/// </summary>
public class FolderMemoryTests
{
    [Test]
    public void With_nothing_kept_a_folder_starts_as_its_list_says()
    {
        using var memory = new FolderMemory();

        memory.Track(new Folder("Production/1")).IsExpanded.Should().BeTrue();
        memory.Track(new Folder("Deleted"), startsOpen: false).IsExpanded.Should().BeFalse();
    }

    [Test]
    public void Every_folder_starts_open_but_those_left_closed_one_new_since_included()
    {
        using var memory = new FolderMemory(closedKeys: ["b"], openKeys: ["a"]);

        memory.Track(new Folder("a")).IsExpanded.Should().BeTrue();
        memory.Track(new Folder("b")).IsExpanded.Should().BeFalse("it was left closed");
        memory.Track(new Folder("new")).IsExpanded.Should().BeTrue("a folder new since opens");
        memory.Track(new Folder("Deleted"), startsOpen: false).IsExpanded.Should().BeTrue(
            "what was kept decides: it wasn't closed when the list was left");
    }

    [Test]
    public void The_folders_a_list_kept_open_before_open_again_and_the_rest_start_closed()
    {
        using var memory = new FolderMemory(openKeys: ["a"]);

        memory.Track(new Folder("a")).IsExpanded.Should().BeTrue();
        memory.Track(new Folder("b")).IsExpanded.Should().BeFalse("it wasn't left open");
    }

    [Test]
    public void The_closed_folders_are_what_is_kept_and_until_the_list_is_filled_what_was_kept_before()
    {
        using var memory = new FolderMemory(["c", "a"]);
        Folder[] folders = [memory.Track(new Folder("a")), memory.Track(new Folder("b")), memory.Track(new Folder("c"))];

        memory.ClosedKeys(folders, isFilled: false).Should().Equal("a", "c");

        folders[1].IsExpanded = false;
        folders[2].IsExpanded = true;

        memory.ClosedKeys(folders, isFilled: true).Should().Equal("a", "b");
    }

    [Test]
    public void A_list_that_kept_its_open_folders_has_no_closed_ones_to_keep_until_it_is_filled()
    {
        using var memory = new FolderMemory(openKeys: ["a"]);
        Folder[] folders = [memory.Track(new Folder("a")), memory.Track(new Folder("b"))];

        memory.ClosedKeys(folders, isFilled: false).Should().BeEmpty("which folders it has isn't known yet");
        memory.ClosedKeys(folders, isFilled: true).Should().Equal("b");
    }

    [Test]
    public void With_nothing_kept_the_folders_that_start_closed_are_what_a_list_left_untouched_keeps()
    {
        using var memory = new FolderMemory();
        Folder[] folders = [memory.Track(new Folder("Production")), memory.Track(new Folder("Deleted"), startsOpen: false)];

        memory.ClosedKeys(folders, isFilled: false).Should().Equal("Deleted");
        memory.ClosedKeys(folders, isFilled: true).Should().Equal(["Deleted"], "so nothing is written for a list nobody touched");
    }

    [Test]
    public void A_folder_opened_or_closed_is_reported_and_one_announced_unchanged_is_not()
    {
        using var memory = new FolderMemory();
        var folder = memory.Track(new Folder("a"));
        var reports = 0;
        using var subscription = memory.ExpansionChanged.Subscribe(_ => reports++);

        folder.IsExpanded = false;
        folder.Announce();
        folder.IsExpanded = true;
        folder.Announce();

        reports.Should().Be(2);
    }

    [Test]
    public void Read_takes_the_closed_folders_kept_and_failing_those_the_open_ones()
    {
        var store = new InMemoryViewStateStore();
        store.WriteList("Test.OpenFolders", ["a"]);

        using (var memory = FolderMemory.Read(store, "Test.ClosedFolders", "Test.OpenFolders"))
        {
            memory.Track(new Folder("a")).IsExpanded.Should().BeTrue();
            memory.Track(new Folder("b")).IsExpanded.Should().BeFalse("only a was left open");
        }

        store.WriteList("Test.ClosedFolders", ["a"]);

        using (var memory = FolderMemory.Read(store, "Test.ClosedFolders", "Test.OpenFolders"))
        {
            memory.Track(new Folder("a")).IsExpanded.Should().BeFalse();
            memory.Track(new Folder("b")).IsExpanded.Should().BeTrue("the closed folders kept decide");
        }
    }

    private sealed class Folder(string key) : IListFolder
    {
        private bool _isExpanded;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; } = key;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    Announce();
                }
            }
        }

        /// <summary>Raises IsExpanded unchanged, as a tree tells the tree view again about a folder.</summary>
        public void Announce() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
    }
}
