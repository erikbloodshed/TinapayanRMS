using System.Collections.ObjectModel;
using System.ComponentModel;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Web;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

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

    /// <summary>Each row's click handler, by the id the page knows it by: a different id is a handler swapped in.</summary>
    private static List<string?> ClickHandlers(IRenderedComponent<FolderTree<Node>> tree) =>
        [.. tree.FindAll(".tree-row").Select(row => row.GetAttribute("blazor:onclick"))];

    [Test]
    public async Task A_rows_handlers_are_kept_when_the_tree_is_drawn_again()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var tree = Render(context);
        var before = ClickHandlers(tree);

        // Drawn again, as its screen draws it for any change it shows, with the rows as they were.
        tree.Render();

        before.Should().HaveCount(4).And.NotContainNulls().And.OnlyHaveUniqueItems();
        ClickHandlers(tree).Should().Equal(before);
    }

    [Test]
    public async Task A_row_added_gets_its_own_handlers_and_the_others_keep_theirs()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var tree = Render(context);
        var before = ClickHandlers(tree);

        _breads.Children!.Add(new Node("Spanish Bread"));
        tree.Render();

        var after = ClickHandlers(tree);
        Titles(tree).Should().Equal("Breads/", "Asado Roll", "Pandesal", "Spanish Bread", "Cakes/");
        after.Should().HaveCount(5).And.OnlyHaveUniqueItems();
        after.Take(3).Should().Equal(before.Take(3));
        after[4].Should().Be(before[3]);
    }

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

    [Test]
    public async Task A_row_being_named_isnt_dragged_and_the_keys_and_clicks_in_its_box_stay_there()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var pandesal = _breads.Children![1];
        var shortcuts = 0;
        var tree = context.Render<FolderTree<Node>>(parameters => parameters
            .Add(t => t.Roots, [_breads, _cakes])
            .Add(t => t.ChildrenOf, node => node.Children)
            .Add(t => t.IsExpanded, node => node.IsExpanded)
            .Add(t => t.SetExpanded, (node, open) => node.IsExpanded = open)
            .Add(t => t.Selected, _selected)
            .Add(t => t.KeyOf, node => node.Name)
            .Add(t => t.CanDrag, _ => true)
            .Add(t => t.IsEditing, node => node == pandesal)
            .Add(t => t.Shortcut, _ =>
            {
                shortcuts++;
                return null;
            })
            .Add(t => t.RowTemplate, node => builder =>
            {
                if (node == pandesal)
                {
                    builder.OpenComponent<TreeNameBox>(0);
                    builder.AddComponentParameter(1, nameof(TreeNameBox.InitialText), node.Name);
                    builder.AddComponentParameter(2, nameof(TreeNameBox.SettleTime), TimeSpan.Zero);
                    builder.CloseComponent();
                }
                else
                {
                    builder.AddMarkupContent(0, $"<span class=\"title\">{node.Name}</span>");
                }
            }));

        var row = tree.FindAll(".tree-row")[2];
        row.ClassList.Should().Contain("editing");
        row.GetAttribute("draggable").Should().Be("false", "a row that can be dragged keeps the mouse from selecting its text");
        tree.FindAll(".tree-row")[1].GetAttribute("draggable").Should().Be("true");

        var input = tree.Find(".tree-row.editing input");
        await input.KeyDownAsync(Key.Down);
        await input.KeyDownAsync(Key.Home);
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "F2" });
        _selected.Should().BeEmpty("the keys are the box's");
        shortcuts.Should().Be(0);

        await tree.FindAll(".tree-row")[2].ClickAsync(new MouseEventArgs());
        _selected.Should().BeEmpty("a click on the row being named, around its box, picks nothing either");

        _selected.Add(pandesal);
        await tree.Find(".folder-tree").KeyDownAsync(new KeyboardEventArgs { Key = "F2" });
        shortcuts.Should().Be(0, "F2 on the row being named renames nothing more");
    }

    /// <summary>What F2 runs, counted.</summary>
    private sealed class CountingCommand : System.Windows.Input.ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Runs++;
    }

    /// <summary>The tree, with a record's F2 counted and a slow second click's wait held until <paramref name="wait"/> ends.</summary>
    private IRenderedComponent<FolderTree<Node>> RenderRenaming(BunitContext context, CountingCommand rename, Task wait) =>
        context.Render<FolderTree<Node>>(parameters => parameters
            .Add(tree => tree.Roots, [_breads, _cakes])
            .Add(tree => tree.ChildrenOf, node => node.Children)
            .Add(tree => tree.IsExpanded, node => node.IsExpanded)
            .Add(tree => tree.SetExpanded, (node, open) => node.IsExpanded = open)
            .Add(tree => tree.Selected, _selected)
            .Add(tree => tree.KeyOf, node => node.Name)
            .Add(tree => tree.RowTemplate, node => $"<span class=\"title\">{node.Name}</span>")
            .Add(tree => tree.Shortcut, key => key == "F2" ? new TreeMenuItem("Rename", rename) : null)
            .Add(tree => tree.RenamesOnSecondClick, node => node.Children is null)
            .Add(tree => tree.SecondClickWait, cancellation => wait.WaitAsync(cancellation)));

    private static Task ClickRowAsync(IRenderedComponent<FolderTree<Node>> tree, int row, long detail = 1) =>
        tree.FindAll(".tree-row")[row].ClickAsync(new MouseEventArgs { Detail = detail });

    [Test]
    public async Task A_slow_second_click_anywhere_on_the_picked_records_row_renames_it_once_the_wait_is_over()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var rename = new CountingCommand();
        var wait = new TaskCompletionSource();
        var tree = RenderRenaming(context, rename, wait.Task);

        // On the row, not only on its name text, which is short beside the row: on the text alone it worked now and then.
        await ClickRowAsync(tree, 1);
        _selected.Should().Equal(_breads.Children![0]);
        await ClickRowAsync(tree, 1);
        rename.Runs.Should().Be(0, "it waits, in case the click is a double-click's");

        wait.SetResult();

        tree.WaitForAssertion(() => rename.Runs.Should().Be(1));
    }

    [Test]
    public async Task A_double_click_a_key_or_a_folder_renames_nothing()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var rename = new CountingCommand();
        var wait = new TaskCompletionSource();
        var tree = RenderRenaming(context, rename, wait.Task);

        // A double-click's second click (Detail 2) calls the first's off, picked already or not (the user's choice).
        await ClickRowAsync(tree, 1);
        await ClickRowAsync(tree, 1, detail: 2);
        await ClickRowAsync(tree, 1);
        await ClickRowAsync(tree, 1, detail: 2);

        // A key pressed while a slow second click waits.
        await ClickRowAsync(tree, 1);
        await tree.Find(".folder-tree").KeyDownAsync(Key.Enter);

        // A folder's row twice, which opens and closes it.
        await ClickRowAsync(tree, 3);
        await ClickRowAsync(tree, 3);
        _cakes.IsExpanded.Should().BeFalse("a folder's clicks open and close it");

        wait.SetResult();
        await tree.InvokeAsync(() => { });

        rename.Runs.Should().Be(0);
    }

    [Test]
    public async Task The_cursor_leaving_the_tree_while_a_slow_second_click_waits_renames_nothing()
    {
        await using var context = AppServices.NewContext();
        _breads.IsExpanded = true;
        var rename = new CountingCommand();
        var wait = new TaskCompletionSource();
        var tree = RenderRenaming(context, rename, wait.Task);

        // A field of the form clicked straight after the second click: the name box would take the cursor from it.
        await ClickRowAsync(tree, 1);
        await ClickRowAsync(tree, 1);
        await tree.Find(".folder-tree").FocusOutAsync(new());

        wait.SetResult();
        await tree.InvokeAsync(() => { });

        rename.Runs.Should().Be(0);
    }
}
