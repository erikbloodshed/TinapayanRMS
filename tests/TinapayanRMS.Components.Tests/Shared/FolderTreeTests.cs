using System.Collections.ObjectModel;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Web;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// FolderTree: the Raw Materials, Sub-Assemblies and Products lists. The screen's ViewModel owns the folders, which are
/// open and the rows picked; the tree draws them and changes them as rows are clicked and keys pressed.
/// </summary>
public class FolderTreeTests
{
    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;

        /// <summary>Null for a record, which holds nothing.</summary>
        public List<Node>? Children { get; } = children.Length > 0 || name.EndsWith('/') ? [.. children] : null;

        public bool IsExpanded { get; set; }
    }

    private readonly Node _breads = new("Breads/", new Node("Asado Roll"), new Node("Pandesal"));
    private readonly Node _cakes = new("Cakes/");
    private readonly List<object> _selected = [];

    private IRenderedComponent<FolderTree<Node>> Render(BunitContext context) =>
        context.Render<FolderTree<Node>>(parameters => parameters
            .Add(tree => tree.Roots, [_breads, _cakes])
            .Add(tree => tree.ChildrenOf, node => node.Children)
            .Add(tree => tree.IsExpanded, node => node.IsExpanded)
            .Add(tree => tree.SetExpanded, (node, open) => node.IsExpanded = open)
            .Add(tree => tree.Selected, _selected)
            .Add(tree => tree.KeyOf, node => node.Name)
            .Add(tree => tree.RowTemplate, node => $"<span class=\"title\">{node.Name}</span>"));

    private static IEnumerable<string> Titles(IRenderedComponent<FolderTree<Node>> tree) =>
        tree.FindAll(".tree-row .title").Select(title => title.TextContent);

    [Test]
    public async Task A_closed_folders_rows_arent_drawn()
    {
        await using var context = AppServices.NewContext();

        var tree = Render(context);

        Titles(tree).Should().Equal("Breads/", "Cakes/");
        tree.Find(".tree-row").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Test]
    public async Task Clicking_a_folder_picks_it_and_opens_or_closes_it()
    {
        await using var context = AppServices.NewContext();
        var tree = Render(context);

        await tree.Find(".tree-row").ClickAsync(new MouseEventArgs());

        _breads.IsExpanded.Should().BeTrue();
        _selected.Should().Equal(_breads);
        Titles(tree).Should().Equal("Breads/", "Asado Roll", "Pandesal", "Cakes/");

        await tree.Find(".tree-row").ClickAsync(new MouseEventArgs());

        _breads.IsExpanded.Should().BeFalse();
        Titles(tree).Should().Equal("Breads/", "Cakes/");
    }

    [Test]
    public async Task Clicking_a_record_picks_it_alone_and_Ctrl_adds_another()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var tree = Render(context);

        await tree.FindAll(".tree-row")[1].ClickAsync(new MouseEventArgs());
        _selected.Should().Equal(_breads.Children![0]);
        tree.FindAll(".tree-row.selected .title").Select(title => title.TextContent).Should().Equal("Asado Roll");

        await tree.FindAll(".tree-row")[2].ClickAsync(new MouseEventArgs { CtrlKey = true });
        _selected.Should().Equal(_breads.Children[0], _breads.Children[1]);

        await tree.FindAll(".tree-row")[2].ClickAsync(new MouseEventArgs());
        _selected.Should().Equal(_breads.Children[1]);
    }

    [Test]
    public async Task Shift_click_picks_every_row_from_the_first_picked_to_the_one_clicked()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var tree = Render(context);

        await tree.FindAll(".tree-row")[1].ClickAsync(new MouseEventArgs());
        await tree.FindAll(".tree-row")[3].ClickAsync(new MouseEventArgs { ShiftKey = true });

        _selected.Should().Equal(_breads.Children![0], _breads.Children[1], _cakes);
    }

    [Test]
    public async Task The_arrows_move_through_the_rows_drawn_and_open_and_close_folders()
    {
        await using var context = AppServices.NewContext();
        var tree = Render(context);
        var list = tree.Find(".folder-tree");

        await list.KeyDownAsync(Key.Down);
        _selected.Should().Equal(_breads);

        await list.KeyDownAsync(Key.Right);
        _breads.IsExpanded.Should().BeTrue();

        await list.KeyDownAsync(Key.Down);
        _selected.Should().Equal(_breads.Children![0]);

        // Left on a record goes up to its folder; Left again closes the folder.
        await list.KeyDownAsync(Key.Left);
        _selected.Should().Equal(_breads);
        await list.KeyDownAsync(Key.Left);
        _breads.IsExpanded.Should().BeFalse();

        await list.KeyDownAsync(Key.End);
        _selected.Should().Equal(_cakes);
        await list.KeyDownAsync(Key.Home);
        _selected.Should().Equal(_breads);
    }

    [Test]
    public async Task A_right_click_on_a_row_or_the_list_doesnt_open_the_browsers_menu()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;

        var tree = Render(context);

        // A row keeps its right-click from the list, so the list's preventDefault doesn't cover it: each needs its own.
        tree.Find(".folder-tree").HasAttribute("blazor:oncontextmenu:preventdefault").Should().BeTrue();
        tree.FindAll(".tree-row").Should().HaveCount(4)
            .And.OnlyContain(row => row.HasAttribute("blazor:oncontextmenu:preventdefault"));
    }

    /// <summary>A row as a screen's tree has them: it says when its title changes, and a folder when its rows do.</summary>
    private sealed class WatchedNode(string name, bool isFolder = false) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        } = name;

        public ObservableCollection<WatchedNode>? Children { get; } = isFolder ? [] : null;
    }

    [Test]
    public async Task Rows_that_change_together_are_drawn_once()
    {
        await using var context = AppServices.NewContext();
        var folder = new WatchedNode("Breads", isFolder: true);
        var drawings = 0;
        var tree = context.Render<FolderTree<WatchedNode>>(parameters => parameters
            .Add(t => t.Roots, [folder])
            .Add(t => t.ChildrenOf, node => node.Children)
            .Add(t => t.IsExpanded, _ => true)
            .Add(t => t.SetExpanded, (_, _) => { })
            .Add(t => t.Selected, _selected)
            .Add(t => t.KeyOf, node => node.Name)
            .Add(t => t.RowTemplate, node =>
            {
                // The folder's row is drawn once each time the tree is.
                if (ReferenceEquals(node, folder))
                {
                    drawings++;
                }

                return $"<span class=\"title\">{node.Name}</span>";
            }));
        var before = drawings;

        // A refresh: the folder's rows arrive, and its title changes with their count.
        await tree.InvokeAsync(() =>
        {
            for (var i = 1; i <= 40; i++)
            {
                folder.Children!.Add(new WatchedNode($"Bread {i}"));
                folder.Name = $"Breads ({i})";
            }
        });

        tree.WaitForAssertion(() => tree.FindAll(".tree-row .title").Should().HaveCount(41));
        tree.Find(".tree-row .title").TextContent.Should().Be("Breads (40)");
        (drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_tree_left_is_let_go_though_its_folders_live_on()
    {
        await using var context = AppServices.NewContext();

        // The ViewModel's folders and rows, which outlive every list drawn from them.
        var folder = new WatchedNode("Breads", isFolder: true);
        folder.Children!.Add(new WatchedNode("Pandesal"));

        var left = context.ShowAndLeave<FolderTree<WatchedNode>>(
            parameters => parameters
                .Add(t => t.Roots, [folder])
                .Add(t => t.ChildrenOf, node => node.Children)
                .Add(t => t.IsExpanded, _ => true)
                .Add(t => t.SetExpanded, (_, _) => { })
                .Add(t => t.Selected, _selected)
                .Add(t => t.KeyOf, node => node.Name)
                .Add(t => t.RowTemplate, node => $"<span class=\"title\">{node.Name}</span>"),
            whileShown: tree => tree.FindAll(".tree-row").Should().HaveCount(2));

        // A refresh once the list has gone: a row arrives, and the folder's title changes with the count.
        await context.Renderer.Dispatcher.InvokeAsync(() =>
        {
            folder.Children!.Add(new WatchedNode("Asado Roll"));
            folder.Name = "Breads (2)";
        });

        left.IsCollected().Should().BeTrue("the folders outlive every visit to the list, and must hold on to none of the trees drawn from them");
        GC.KeepAlive(folder);
    }

    [Test]
    public async Task Enter_opens_or_closes_the_folder_picked()
    {
        await using var context = AppServices.NewContext();
        var tree = Render(context);
        var list = tree.Find(".folder-tree");
        await list.KeyDownAsync(Key.Down);

        await list.KeyDownAsync(Key.Enter);
        _breads.IsExpanded.Should().BeTrue();

        await list.KeyDownAsync(Key.Enter);
        _breads.IsExpanded.Should().BeFalse();
    }
}
