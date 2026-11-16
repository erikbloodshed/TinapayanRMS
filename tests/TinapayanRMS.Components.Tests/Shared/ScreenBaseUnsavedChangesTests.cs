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
/// ScreenBase keeps a form with unsaved changes from being left: a move to another address asks first, and Cancel stays.
/// NavigateTo hands the handler the address as it was given, so one given relative to the app (products/26, as Find and
/// the screens give theirs) must be asked about as surely as one the browser gives in full.
/// </summary>
public class ScreenBaseUnsavedChangesTests
{
    private sealed class Form : ScreenState, IUnsavedChangesScreen
    {
        public Form(ILogger logger)
            : base(logger)
        {
        }

        public bool HasUnsavedChanges { get; set; } = true;

        /// <summary>What the user answers: true goes on (Save or Don't Save), false is Cancel.</summary>
        public bool Answer { get; set; }

        public List<bool> Asked { get; } = [];

        public Task<bool> ResolveUnsavedChangesAsync(bool revert)
        {
            Asked.Add(revert);
            return Task.FromResult(Answer);
        }
    }

    private sealed class FormScreen : ScreenBase<Form>
    {
        protected override ScreenCommand? LoadCommand => null;

        public void ShowRecord(string path) => MoveAddressTo(path);

        protected override bool IsOwnAddress(string path) => path.StartsWith("products", StringComparison.Ordinal);

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "form");
    }

    private static (IRenderedComponent<FormScreen> Screen, Form Form, NavigationManager Navigation) Render(BunitContext context)
    {
        context.Services.AddScoped<Form>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("products/13");
        var screen = context.Render<FormScreen>();
        return (screen, context.Services.GetRequiredService<Form>(), navigation);
    }

    [Test]
    public async Task A_move_given_relative_to_the_app_is_asked_about_and_Cancel_stays()
    {
        await using var context = AppServices.NewContext();
        var (screen, form, navigation) = Render(context);

        await screen.InvokeAsync(() => navigation.NavigateTo("raw-materials/26"));

        form.Asked.Should().Equal(true);
        navigation.Uri.Should().EndWith("/products/13");
    }

    [Test]
    public async Task A_move_to_another_record_of_the_screen_keeps_the_form_for_it()
    {
        await using var context = AppServices.NewContext();
        var (screen, form, navigation) = Render(context);
        form.Answer = true;

        await screen.InvokeAsync(() => navigation.NavigateTo("products/26"));

        form.Asked.Should().Equal(false);
        navigation.Uri.Should().EndWith("/products/26");
    }

    [Test]
    public async Task The_screen_moving_its_own_address_isnt_asked_about()
    {
        await using var context = AppServices.NewContext();
        var (screen, form, navigation) = Render(context);

        await screen.InvokeAsync(() => screen.Instance.ShowRecord("products/26"));

        form.Asked.Should().BeEmpty();
        navigation.Uri.Should().EndWith("/products/26");
    }

    [Test]
    public async Task A_form_with_nothing_unsaved_is_left_without_asking()
    {
        await using var context = AppServices.NewContext();
        var (screen, form, navigation) = Render(context);
        form.HasUnsavedChanges = false;

        await screen.InvokeAsync(() => navigation.NavigateTo("raw-materials/26"));

        form.Asked.Should().BeEmpty();
        navigation.Uri.Should().EndWith("/raw-materials/26");
    }
}
