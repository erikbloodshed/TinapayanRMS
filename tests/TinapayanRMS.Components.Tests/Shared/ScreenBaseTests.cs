using System.Collections.ObjectModel;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// ScreenBase: a screen draws itself again when its state, one of its lists or one of its commands changes, and
/// once for however many of them change together, as they do in a load.
/// </summary>
public class ScreenBaseTests
{
    private sealed class Lines : ScreenState
    {
        private string _name = "";

        public Lines(ILogger logger)
            : base(logger)
        {
            SaveCommand = ScreenCommand.Of(() => { }, () => _canSave);
            PrintCommand = new ScreenCommand(() => PrintDone.Task);
        }

        public string Name
        {
            get => _name;
            set => RaiseAndSetIfChanged(ref _name, value);
        }

        public ObservableCollection<int> Rows { get; } = [];

        public TableRows<int> Table { get; } = [];

        private bool _canSave;

        /// <summary>Lets Save run, saying so through the command alone, as a command whose test changed does.</summary>
        public void AllowSave()
        {
            _canSave = true;
            SaveCommand.Refresh();
        }

        public ScreenCommand SaveCommand { get; }

        /// <summary>What ends a Print run.</summary>
        public TaskCompletionSource PrintDone { get; } = new();

        /// <summary>One of the app's own commands, which can't run while it runs.</summary>
        public ScreenCommand PrintCommand { get; }

        /// <summary>A part, as a tab's is, with a command of its own.</summary>
        public TestScreenPart Part { get; } = new();
    }

    /// <summary>
    /// A screen that shows its state's name, rows, table and whether Save can run, and whether its part's command can,
    /// and counts its drawings.
    /// </summary>
    private sealed class LinesScreen : ScreenBase<Lines>
    {
        public int Drawings { get; private set; }

        protected override ScreenCommand? LoadCommand => null;

        public Task DrawNowAsync() => RedrawNowAsync();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Drawings++;
            builder.OpenElement(0, "p");
            builder.AddContent(1, $"{State!.Name}|{State.Rows.Count}|{State.SaveCommand.CanExecute()}");
            builder.CloseElement();
            builder.OpenElement(2, "span");
            builder.AddContent(3, string.Join(",", State.Table));
            builder.CloseElement();
            builder.OpenElement(4, "em");
            builder.AddContent(5, State.Part.RemoveCommand.CanExecute());
            builder.CloseElement();
            builder.OpenElement(6, "b");
            builder.AddContent(7, State.PrintCommand.CanExecute());
            builder.CloseElement();
        }
    }

    /// <summary>A screen state with worked-out properties, which count how often they're read.</summary>
    private sealed class WorkedOut(ILogger logger) : ScreenState(logger)
    {
        public int Reads { get; private set; }

        public ObservableCollection<int> Rows { get; } = [];

        public string Caption => $"{++Reads} read";

        public IReadOnlyList<int> Picked => [.. Rows.Where(_ => ++Reads > 0)];
    }

    /// <summary>A screen that shows only its state's rows.</summary>
    private sealed class WorkedOutScreen : ScreenBase<WorkedOut>
    {
        protected override ScreenCommand? LoadCommand => null;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, State!.Rows.Count);
            builder.CloseElement();
        }
    }

    [Test]
    public async Task Finding_what_to_watch_reads_no_worked_out_property_and_still_watches_the_lists()
    {
        await using var context = AppServices.NewContext();
        context.Services.AddScoped<WorkedOut>();
        var screen = context.Render<WorkedOutScreen>();
        var state = context.Services.GetRequiredService<WorkedOut>();

        await screen.InvokeAsync(() => state.Rows.Add(1));

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("1"));
        state.Reads.Should().Be(0);
    }

    private static (IRenderedComponent<LinesScreen> Screen, Lines State) Render(BunitContext context)
    {
        context.Services.AddScoped<Lines>();
        var screen = context.Render<LinesScreen>();

        // The screen's first drawings settle.
        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|0|False"));
        return (screen, context.Services.GetRequiredService<Lines>());
    }

    [Test]
    public async Task A_load_that_changes_hundreds_of_things_is_drawn_once()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);
        var before = screen.Instance.Drawings;

        await screen.InvokeAsync(() =>
        {
            for (var i = 1; i <= 200; i++)
            {
                state.Name = $"Product {i}";
                state.Rows.Add(i);
            }
        });

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("Product 200|200|False"));
        (screen.Instance.Drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_list_changing_alone_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);

        await screen.InvokeAsync(() => state.Rows.Add(1));

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|1|False"));
    }

    [Test]
    public async Task A_table_replaced_in_one_go_redraws_the_screen_once()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);
        var before = screen.Instance.Drawings;

        await screen.InvokeAsync(() => state.Table.ReplaceWith([3, 1, 2]));

        screen.WaitForAssertion(() => screen.Find("span").TextContent.Should().Be("3,1,2"));
        (screen.Instance.Drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_command_becoming_able_to_run_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);

        await screen.InvokeAsync(state.AllowSave);

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|0|True"));
    }

    [Test]
    public async Task A_screen_command_starting_and_ending_a_run_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);
        screen.Find("b").TextContent.Should().Be("True");

        var printing = Task.CompletedTask;
        await screen.InvokeAsync(() => { printing = state.PrintCommand.ExecuteAsync(); });
        screen.WaitForAssertion(() => screen.Find("b").TextContent.Should().Be("False"));

        await screen.InvokeAsync(state.PrintDone.SetResult);
        await printing;
        screen.WaitForAssertion(() => screen.Find("b").TextContent.Should().Be("True"));
    }

    // A part's commands are its own (the Products screen's Packaging tab's Remove Line, say), so the screen watches them
    // as it watches its state's: a button bound to one goes on and off with nothing on the screen state itself changing.
    [Test]
    public async Task A_command_of_a_part_becoming_able_to_run_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);

        await screen.InvokeAsync(state.Part.AllowRemove);

        screen.WaitForAssertion(() => screen.Find("em").TextContent.Should().Be("True"));
    }

    [Test]
    public async Task A_change_from_another_thread_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);

        await Task.Run(() => state.Name = "Cheese Roll");

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("Cheese Roll|0|False"));
    }

    [Test]
    public async Task A_screen_drawn_at_once_isnt_drawn_again_for_the_same_changes()
    {
        await using var context = AppServices.NewContext();
        var (screen, state) = Render(context);
        var before = screen.Instance.Drawings;

        // What undo does: the changes, then the drawing, before the browser is told it's done.
        var drawnAtOnce = 0;
        await screen.InvokeAsync(async () =>
        {
            state.Name = "Cheese Roll";
            state.Rows.Add(1);
            await screen.Instance.DrawNowAsync();
            drawnAtOnce = screen.Instance.Drawings - before;
        });

        // A turn of the dispatcher later, when the redraw those changes queued would have been drawn.
        await screen.InvokeAsync(async () => await Task.Yield());

        drawnAtOnce.Should().Be(1);
        screen.Find("p").TextContent.Should().Be("Cheese Roll|1|False");
        (screen.Instance.Drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_screen_left_is_let_go_though_its_viewmodel_lives_on()
    {
        await using var context = AppServices.NewContext();
        context.Services.AddScoped<Lines>();
        var state = context.Services.GetRequiredService<Lines>();

        // Checked as drawn, not waited for: bUnit's waits hold on to the component they wait on.
        var left = context.ShowAndLeave<LinesScreen>(whileShown: screen =>
            screen.Find("p").TextContent.Should().Be("|0|False"));

        // The screen state goes on changing once its screen has gone, as a load the screen started does.
        await context.Renderer.Dispatcher.InvokeAsync(() =>
        {
            state.Name = "Cheese Roll";
            state.Rows.Add(1);
            state.Table.ReplaceWith([1]);
            state.AllowSave();
            state.Part.AllowRemove();
        });

        left.IsCollected().Should().BeTrue("the screen state outlives every visit to its screen, and must hold on to none of them");
        GC.KeepAlive(state);
    }
}
