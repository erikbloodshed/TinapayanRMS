using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using ILogger = Serilog.ILogger;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// ScreenBase: a screen draws itself again when its ViewModel, one of its lists or one of its commands changes, and
/// once for however many of them change together, as they do in a load.
/// </summary>
public class ScreenBaseTests
{
    private sealed class Lines : ViewModelBase
    {
        private string _name = "";

        public Lines(ILogger logger)
            : base(logger)
        {
            SaveCommand = ReactiveCommand.Create(() => { }, CanSave);
        }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public ObservableCollection<int> Rows { get; } = [];

        public TableRows<int> Table { get; } = [];

        public BehaviorSubject<bool> CanSave { get; } = new(false);

        public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    }

    /// <summary>A screen that shows its ViewModel's name, rows, table and whether Save can run, and counts its drawings.</summary>
    private sealed class LinesScreen : ScreenBase<Lines>
    {
        public int Drawings { get; private set; }

        protected override ReactiveCommand<RxVoid, RxVoid>? LoadCommand => null;

        public Task DrawNowAsync() => RedrawNowAsync();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Drawings++;
            builder.OpenElement(0, "p");
            builder.AddContent(1, $"{ViewModel!.Name}|{ViewModel.Rows.Count}|{CanRun(ViewModel.SaveCommand)}");
            builder.CloseElement();
            builder.OpenElement(2, "span");
            builder.AddContent(3, string.Join(",", ViewModel.Table));
            builder.CloseElement();
        }
    }

    private static (IRenderedComponent<LinesScreen> Screen, Lines ViewModel) Render(BunitContext context)
    {
        context.Services.AddScoped<Lines>();
        var screen = context.Render<LinesScreen>();

        // The screen's first drawings settle (ReactiveUI's base draws it once more as it starts watching).
        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|0|False"));
        return (screen, context.Services.GetRequiredService<Lines>());
    }

    [Test]
    public async Task A_load_that_changes_hundreds_of_things_is_drawn_once()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);
        var before = screen.Instance.Drawings;

        await screen.InvokeAsync(() =>
        {
            for (var i = 1; i <= 200; i++)
            {
                viewModel.Name = $"Product {i}";
                viewModel.Rows.Add(i);
            }
        });

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("Product 200|200|False"));
        (screen.Instance.Drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_list_changing_alone_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);

        await screen.InvokeAsync(() => viewModel.Rows.Add(1));

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|1|False"));
    }

    [Test]
    public async Task A_table_replaced_in_one_go_redraws_the_screen_once()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);
        var before = screen.Instance.Drawings;

        await screen.InvokeAsync(() => viewModel.Table.ReplaceWith([3, 1, 2]));

        screen.WaitForAssertion(() => screen.Find("span").TextContent.Should().Be("3,1,2"));
        (screen.Instance.Drawings - before).Should().Be(1);
    }

    [Test]
    public async Task A_command_becoming_able_to_run_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);

        await screen.InvokeAsync(() => viewModel.CanSave.OnNext(true));

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("|0|True"));
    }

    [Test]
    public async Task A_change_from_another_thread_redraws_the_screen()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);

        await Task.Run(() => viewModel.Name = "Cheese Roll");

        screen.WaitForAssertion(() => screen.Find("p").TextContent.Should().Be("Cheese Roll|0|False"));
    }

    [Test]
    public async Task A_screen_drawn_at_once_isnt_drawn_again_for_the_same_changes()
    {
        await using var context = AppServices.NewContext();
        var (screen, viewModel) = Render(context);
        var before = screen.Instance.Drawings;

        // What undo does: the changes, then the drawing, before the browser is told it's done.
        var drawnAtOnce = 0;
        await screen.InvokeAsync(async () =>
        {
            viewModel.Name = "Cheese Roll";
            viewModel.Rows.Add(1);
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
        var viewModel = context.Services.GetRequiredService<Lines>();

        // Checked as drawn, not waited for: bUnit's waits hold on to the component they wait on.
        var left = context.ShowAndLeave<LinesScreen>(whileShown: screen =>
            screen.Find("p").TextContent.Should().Be("|0|False"));

        // The ViewModel goes on changing once its screen has gone, as a load the screen started does.
        await context.Renderer.Dispatcher.InvokeAsync(() =>
        {
            viewModel.Name = "Cheese Roll";
            viewModel.Rows.Add(1);
            viewModel.Table.ReplaceWith([1]);
            viewModel.CanSave.OnNext(true);
        });

        left.IsCollected().Should().BeTrue("the ViewModel outlives every visit to its screen, and must hold on to none of them");
        GC.KeepAlive(viewModel);
    }
}
