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
/// RecordViewBase gives a record view's tabs an address each: the record in the address opens once the view's load is back,
/// a later address (Back, Forward, a link) opens its record there and then, and the tab showing goes into the address, a
/// new record's by the tab's own number, so Back returns to the one before.
/// </summary>
public class RecordViewBaseTests
{
    private sealed class Thing(ILogger logger) : ScreenState(logger), ITabForm, IDisposable
    {
        public int RecordId
        {
            get;
            set => RaiseAndSetIfChanged(ref field, value);
        }

        public string TabTitle => $"Thing {RecordId}";

        public bool HasUnsavedChanges => false;

        public Task<bool> ResolveUnsavedChangesAsync(bool revert) => Task.FromResult(true);

        public void FollowOwnSaveOf(IEnumerable<int> ids)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Things : RecordViewState<Thing>
    {
        private readonly ILogger _logger;

        public Things(ILogger logger)
            : base(logger)
        {
            _logger = logger;
            Show(AddTab(isPreview: true));
        }

        public List<int> OpenedOnLoad { get; } = [];

        public override bool IsOpeningRecord => false;

        public override ScreenCommand LoadCommand { get; } = ScreenCommand.Of(() => { });

        public override ScreenCommand NewCommand { get; } = ScreenCommand.Of(() => { });

        protected override Thing NewForm() => new(_logger);

        public override bool OpenByName(string text) => false;

        public override void OpenRecord(int id) => OpenInTab(id, keep: false, form => form.RecordId = id);

        public override void OpenRecordOnLoad(int id) => OpenedOnLoad.Add(id);

        public void OpenKept(int id) => OpenInTab(id, keep: true, form => form.RecordId = id);
    }

    private sealed class ThingsPage : RecordViewBase<Things, Thing>
    {
        protected override ScreenCommand? LoadCommand => null;

        protected override string AddressRoot => "things";

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "things");
    }

    private static (IRenderedComponent<ThingsPage> Page, Things Things, NavigationManager Navigation) Render(BunitContext context, int? id)
    {
        context.Services.AddScoped<Things>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(id is { } at ? $"things/{at}" : "things");
        var page = context.Render<ThingsPage>(parameters => parameters.Add(p => p.Id, id));
        return (page, context.Services.GetRequiredService<Things>(), navigation);
    }

    [Test]
    public async Task The_record_in_the_address_opens_once_the_load_is_back_and_only_once()
    {
        await using var context = AppServices.NewContext();
        var (page, things, _) = Render(context, 13);

        // Given the same address again, as the page is once its load is back: nothing more.
        page.Render(parameters => parameters.Add(p => p.Id, 13));

        things.OpenedOnLoad.Should().Equal(13);
        things.Tabs.All.Should().ContainSingle().Which.Form.RecordId.Should().Be(0, "the load opens it, not the address itself");
    }

    [Test]
    public async Task A_later_address_opens_its_record_there_and_then_in_the_preview_tab()
    {
        await using var context = AppServices.NewContext();
        var (page, things, _) = Render(context, 13);

        page.Render(parameters => parameters.Add(p => p.Id, 26));

        things.Form!.RecordId.Should().Be(26);
        things.Tabs.All.Should().ContainSingle().Which.IsPreview.Should().BeTrue();
    }

    [Test]
    public async Task The_tab_showing_goes_into_the_address_a_new_records_by_its_number()
    {
        await using var context = AppServices.NewContext();
        var (page, things, navigation) = Render(context, null);
        var blank = things.Tabs.Active!;
        navigation.Uri.Should().EndWith($"/things/new/{blank.Number}", "the view's bare address takes its tab's");

        await page.InvokeAsync(() => things.OpenKept(26));
        await page.WaitForAssertionAsync(() => navigation.Uri.Should().EndWith("/things/26"));

        await page.InvokeAsync(() => things.Show(things.Tabs.All.Single(tab => tab.Form.RecordId == 0)));
        await page.WaitForAssertionAsync(() => navigation.Uri.Should().EndWith($"/things/new/{blank.Number}"));
    }

    // A link, Back or the activity bar has left the view while its tabs still change (a load running, a pick's details read):
    // the window stays where it went, not back on the view.
    [Test]
    public async Task A_tab_changing_once_the_address_is_another_views_leaves_the_address_alone()
    {
        await using var context = AppServices.NewContext();
        var (page, things, navigation) = Render(context, null);

        navigation.NavigateTo("elsewhere/4");
        await page.InvokeAsync(() => things.OpenKept(26));
        await page.InvokeAsync(() => things.Form!.RecordId = 27);

        navigation.Uri.Should().EndWith("/elsewhere/4");
    }

    [Test]
    public async Task With_every_tab_closed_the_address_is_the_views_own()
    {
        await using var context = AppServices.NewContext();
        var (page, things, navigation) = Render(context, null);

        await page.InvokeAsync(() => things.CloseTabAsync(things.Tabs.Active!));

        await page.WaitForAssertionAsync(() => navigation.Uri.Should().EndWith("/things"));
        things.Form.Should().BeNull();
    }
}
