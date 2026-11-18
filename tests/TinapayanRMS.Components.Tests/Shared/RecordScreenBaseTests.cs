using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// RecordScreenBase gives a record screen an address per record: the record in the address opens once the screen's load
/// is back, a later address (Back, Forward, a link) opens its record there and then, and the record picked goes into the
/// address, so Back returns to the one before.
/// </summary>
public class RecordScreenBaseTests
{
    private sealed class Records : ScreenState, IRecordOpener
    {
        public Records(ILogger logger)
            : base(logger)
        {
        }

        public int? Shown
        {
            get;
            set => RaiseAndSetIfChanged(ref field, value);
        }

        public List<int> Opened { get; } = [];

        public List<int> OpenedOnLoad { get; } = [];

        public void OpenRecord(int id) => Opened.Add(id);

        public void OpenRecordOnLoad(int id) => OpenedOnLoad.Add(id);
    }

    private sealed class RecordScreen : RecordScreenBase<Records>
    {
        protected override ScreenCommand? LoadCommand => null;

        protected override string AddressRoot => "things";

        protected override int? ShownId => State!.Shown;

        protected override string ShownProperty => nameof(Records.Shown);

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "records");
    }

    private static (IRenderedComponent<RecordScreen> Screen, Records Records, NavigationManager Navigation) Render(BunitContext context, int? id)
    {
        context.Services.AddScoped<Records>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(id is { } at ? $"things/{at}" : "things");
        var screen = context.Render<RecordScreen>(parameters => parameters.Add(s => s.Id, id));
        return (screen, context.Services.GetRequiredService<Records>(), navigation);
    }

    [Test]
    public async Task The_record_in_the_address_opens_once_the_load_is_back_and_only_once()
    {
        await using var context = AppServices.NewContext();
        var (screen, records, _) = Render(context, 13);

        // Given the same address again, as the screen is once its load is back: nothing more.
        screen.Render(parameters => parameters.Add(s => s.Id, 13));

        records.OpenedOnLoad.Should().Equal(13);
        records.Opened.Should().BeEmpty();
    }

    [Test]
    public async Task A_later_address_opens_its_record_there_and_then()
    {
        await using var context = AppServices.NewContext();
        var (screen, records, _) = Render(context, 13);

        screen.Render(parameters => parameters.Add(s => s.Id, 26));

        records.Opened.Should().Equal(26);
    }

    [Test]
    public async Task An_address_of_the_record_shown_opens_nothing()
    {
        await using var context = AppServices.NewContext();
        context.Services.AddScoped<Records>();
        context.Services.GetRequiredService<Records>().Shown = 13;
        var screen = context.Render<RecordScreen>(parameters => parameters.Add(s => s.Id, 13));
        var records = context.Services.GetRequiredService<Records>();

        screen.Render(parameters => parameters.Add(s => s.Id, 13));

        records.OpenedOnLoad.Should().BeEmpty();
        records.Opened.Should().BeEmpty();
    }

    [Test]
    public async Task The_record_picked_goes_into_the_address_and_a_new_one_leaves_it()
    {
        await using var context = AppServices.NewContext();
        var (screen, records, navigation) = Render(context, 13);

        await screen.InvokeAsync(() => records.Shown = 26);
        await screen.WaitForAssertionAsync(() => navigation.Uri.Should().EndWith("/things/26"));

        await screen.InvokeAsync(() => records.Shown = 0);
        navigation.Uri.Should().EndWith("/things/26", "a new record has no address of its own");
    }
}
