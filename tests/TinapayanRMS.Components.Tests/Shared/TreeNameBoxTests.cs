using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// TreeNameBox: a list's row's name typed in place. It starts with the name, selected; Enter or leaving it gives back what's
/// typed, Esc gives up, and only the first of them counts.
/// </summary>
public class TreeNameBoxTests
{
    private readonly List<string> _committed = [];
    private int _cancelled;

    private IRenderedComponent<TreeNameBox> Render(BunitContext context) =>
        context.Render<TreeNameBox>(parameters => parameters
            .Add(box => box.InitialText, "Mamon")
            .Add(box => box.Hint, "What it's called")
            .Add(box => box.SettleTime, TimeSpan.Zero)
            .Add(box => box.OnCommit, (string text) => _committed.Add(text))
            .Add(box => box.OnCancel, () => _cancelled++));

    [Test]
    public async Task It_starts_with_the_name_and_takes_the_cursor()
    {
        await using var context = AppServices.NewContext();

        var box = Render(context);

        box.Find("input").GetAttribute("value").Should().Be("Mamon");
        box.Find(".tree-name-box").GetAttribute("title").Should().Be("What it's called");
        context.JSInterop.VerifyInvoke("tinapayan.focus");
    }

    [Test]
    public async Task Enter_gives_back_what_is_typed_once_and_leaving_after_it_gives_nothing_more()
    {
        await using var context = AppServices.NewContext();
        var input = Render(context).Find("input");

        await input.FocusAsync(new());
        await input.InputAsync(new() { Value = "Mamon Tostado" });
        await input.KeyDownAsync(Key.Enter);
        await input.BlurAsync(new());

        _committed.Should().Equal("Mamon Tostado");
        _cancelled.Should().Be(0);
        context.JSInterop.VerifyInvoke("tinapayan.focusClosest");
    }

    [Test]
    public async Task Esc_gives_up_and_leaving_after_it_gives_nothing()
    {
        await using var context = AppServices.NewContext();
        var input = Render(context).Find("input");

        await input.FocusAsync(new());
        await input.InputAsync(new() { Value = "Mamon Tostado" });
        await input.KeyDownAsync(Key.Escape);
        await input.BlurAsync(new());

        _committed.Should().BeEmpty();
        _cancelled.Should().Be(1);
    }

    [Test]
    public async Task Leaving_the_box_gives_back_what_is_typed()
    {
        await using var context = AppServices.NewContext();
        var input = Render(context).Find("input");

        await input.FocusAsync(new());
        await input.InputAsync(new() { Value = "Mamon Tostado" });
        await input.BlurAsync(new());

        _committed.Should().Equal("Mamon Tostado");
    }

    [Test]
    public async Task The_cursor_taken_before_the_box_is_used_is_taken_back_not_counted_as_leaving()
    {
        await using var context = AppServices.NewContext();
        var input = context.Render<TreeNameBox>(parameters => parameters
            .Add(box => box.InitialText, "New product")
            .Add(box => box.SettleTime, TimeSpan.FromHours(1))
            .Add(box => box.OnCommit, (string text) => _committed.Add(text))
            .Add(box => box.OnCancel, () => _cancelled++)).Find("input");

        // A dialog or drop-down closing takes the cursor as the box shows: nothing is named "New product" by it.
        await input.FocusAsync(new());
        await input.BlurAsync(new());

        _committed.Should().BeEmpty();
        context.JSInterop.VerifyInvoke("tinapayan.focusIfAway");

        // Once it's typed into, leaving it gives the name back.
        await input.FocusAsync(new());
        await input.InputAsync(new() { Value = "Pan de Coco" });
        await input.BlurAsync(new());

        _committed.Should().Equal("Pan de Coco");
    }
}
