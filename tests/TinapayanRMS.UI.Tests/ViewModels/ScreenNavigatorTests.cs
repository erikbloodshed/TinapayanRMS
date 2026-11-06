using System.Diagnostics.CodeAnalysis;
using AwesomeAssertions;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// Moving between the main window's screens with no unsaved changes left behind, as MainWindow does it, without a window: the
/// showing screen is a field, and showing another turns back as a tab picked does when that would leave unsaved changes. A
/// form's question is answered by <see cref="FakeForm"/>, which the screens' own tests answer for real.
/// </summary>
[SuppressMessage("Usage", "TUnit0018", Justification = "Each test sets its own fakes' state: TUnit makes a new instance for each test, so none is shared.")]
public class ScreenNavigatorTests
{
    private readonly NavigationHistory _history = new();
    private readonly FakeForm _rawMaterials = new("Raw Materials");
    private readonly FakeForm _products = new("Products");
    private readonly List<string> _events = [];
    private readonly List<Exception> _failures = [];
    private readonly ScreenNavigator _navigator;

    private AppScreen _showing = AppScreen.RawMaterials;
    private int? _rawMaterialOpen = 1;
    private int? _productOpen = 7;
    private Exception? _showFails;

    public ScreenNavigatorTests()
    {
        _rawMaterials.Asked = revert => _events.Add($"asked Raw Materials, revert {revert}");
        _products.Asked = revert => _events.Add($"asked Products, revert {revert}");
        _navigator = new ScreenNavigator(
            _history,
            [
                new(AppScreen.RawMaterials, () => _rawMaterialOpen, id => { _rawMaterialOpen = id; _events.Add($"opened material {id}"); }, _rawMaterials),
                new(AppScreen.Products, () => _productOpen, id => { _productOpen = id; _events.Add($"opened product {id}"); }, _products),
                new(AppScreen.Reports, () => null),
                new(AppScreen.Print, () => null, UnsavedChanges: _products),
            ],
            () => _showing,
            Show,
            _failures.Add);
        _navigator.Start();
    }

    /// <summary>What MainWindow does when a screen is shown: picking its tab either turns back, or it's a move.</summary>
    private void Show(AppScreen screen)
    {
        if (_showFails is { } failure)
        {
            throw failure;
        }

        if (screen == _showing)
        {
            return;
        }

        if (_navigator.UnsavedChangesLeft(_showing, screen) is not null)
        {
            _events.Add($"turned back from {screen}");
            return;
        }

        _navigator.RecordStep(_showing, screen);
        _showing = screen;
        _events.Add($"showed {screen}");
    }

    private Task<bool> Opens(string what)
    {
        _events.Add(what);
        return Task.FromResult(true);
    }

    // A link from one screen to a record on another

    [Test]
    public Task A_link_opens_the_record_before_it_shows_the_screen() => StaThread.RunAsync(async () =>
    {
        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _events.Should().Equal("opened the record", "showed Products");
    });

    [Test]
    public Task A_link_with_nothing_unsaved_asks_nothing() => StaThread.RunAsync(async () =>
    {
        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _rawMaterials.Questions.Should().Be(0);
    });

    [Test]
    public Task A_link_asks_about_the_screen_left_and_goes_on_once_its_answered() => StaThread.RunAsync(async () =>
    {
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = revert =>
        {
            _rawMaterials.HasUnsavedChanges = false;
            return true;
        };

        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _events.Should().Equal("asked Raw Materials, revert True", "opened the record", "showed Products");
    });

    [Test]
    public Task A_link_cancelled_opens_nothing_and_stays() => StaThread.RunAsync(async () =>
    {
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => false;

        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _events.Should().Equal("asked Raw Materials, revert True");
        _showing.Should().Be(AppScreen.RawMaterials);
    });

    [Test]
    public Task A_link_the_other_screen_stays_on_doesnt_show_it() => StaThread.RunAsync(async () =>
    {
        await _navigator.OpenOnAsync(AppScreen.Products, () => Task.FromResult(false));

        _showing.Should().Be(AppScreen.RawMaterials);
    });

    [Test]
    public Task A_link_between_screens_with_the_same_form_asks_nothing() => StaThread.RunAsync(async () =>
    {
        _showing = AppScreen.Products;
        _products.HasUnsavedChanges = true;

        await _navigator.OpenOnAsync(AppScreen.Print, () => Opens("opened the record"));

        _products.Questions.Should().Be(0, "Print prints the form as Products shows it");
        _showing.Should().Be(AppScreen.Print);
    });

    // Nothing awaits a move, so what fails in one has to be said: it used to reach a warning in the log, and the click did nothing.

    [Test]
    public Task A_link_whose_question_fails_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var failure = new InvalidOperationException("The question couldn't be asked.");
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => throw failure;

        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _failures.Should().Equal(failure);
        _showing.Should().Be(AppScreen.RawMaterials);
        _events.Should().NotContain("opened the record");
    });

    [Test]
    public Task A_link_whose_open_fails_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var failure = new InvalidOperationException("The record couldn't be opened.");

        await _navigator.OpenOnAsync(AppScreen.Products, () => throw failure);

        _failures.Should().Equal(failure);
        _showing.Should().Be(AppScreen.RawMaterials);
    });

    [Test]
    public Task A_link_whose_screen_cant_be_shown_says_so() => StaThread.RunAsync(async () =>
    {
        _showFails = new InvalidOperationException("The screen couldn't be built.");

        await _navigator.OpenOnAsync(AppScreen.Products, () => Opens("opened the record"));

        _failures.Should().Equal(_showFails);
    });

    // Back and Forward

    [Test]
    public void Back_with_nowhere_to_go_says_so()
    {
        _navigator.GoBack().Should().BeFalse();
        _navigator.GoForward().Should().BeFalse();
        _showing.Should().Be(AppScreen.RawMaterials);
    }

    [Test]
    public void Back_and_forward_move_between_the_screens_visited()
    {
        Show(AppScreen.Products);

        _navigator.GoBack().Should().BeTrue();
        _showing.Should().Be(AppScreen.RawMaterials);

        _navigator.GoForward().Should().BeTrue();
        _showing.Should().Be(AppScreen.Products);
    }

    [Test]
    public void Back_isnt_recorded_as_a_new_step()
    {
        Show(AppScreen.Products);

        _navigator.GoBack();

        _history.CanGoForward.Should().BeTrue("a new step would have emptied Forward");
        _history.CanGoBack.Should().BeFalse();
    }

    [Test]
    public void A_move_by_the_tab_strip_remembers_the_record_left()
    {
        _rawMaterialOpen = 42;

        Show(AppScreen.Products);

        _history.BackTarget.Should().Be(new ScreenVisit(AppScreen.RawMaterials, 42));
    }

    [Test]
    public void Back_reopens_the_record_the_screen_had_open()
    {
        _rawMaterialOpen = 42;
        Show(AppScreen.Products);
        _rawMaterialOpen = 43;
        _events.Clear();

        _navigator.GoBack();

        _events.Should().Equal("opened material 42", "showed RawMaterials");
    }

    [Test]
    public void Back_reopens_nothing_when_the_record_is_already_open()
    {
        Show(AppScreen.Products);
        _events.Clear();

        _navigator.GoBack();

        _events.Should().Equal("showed RawMaterials");
    }

    [Test]
    public void Back_doesnt_throw_unsaved_changes_away_on_the_screen_it_returns_to()
    {
        _showing = AppScreen.Products;
        _navigator.Start();
        Show(AppScreen.Reports);
        _productOpen = 8;
        _products.HasUnsavedChanges = true;
        _events.Clear();

        _navigator.GoBack();

        _showing.Should().Be(AppScreen.Products);
        _productOpen.Should().Be(8, "its form holds unsaved changes, so it shows as it is");
        _events.Should().Equal("showed Products");
    }

    [Test]
    public void Back_asks_about_the_screen_left_and_a_cancel_leaves_the_history_where_it_was()
    {
        Show(AppScreen.Products);
        _products.HasUnsavedChanges = true;
        _products.Answer = _ => false;

        _navigator.GoBack().Should().BeTrue("it had somewhere to go, and it's the question that's stopped it");

        _products.Questions.Should().Be(1);
        _showing.Should().Be(AppScreen.Products);
        _history.CanGoBack.Should().BeTrue();
        _history.CanGoForward.Should().BeFalse();
    }

    [Test]
    public void Back_takes_the_step_once_the_screen_left_is_answered()
    {
        Show(AppScreen.Products);
        _products.HasUnsavedChanges = true;
        _products.Answer = _ =>
        {
            _products.HasUnsavedChanges = false;
            return true;
        };

        _navigator.GoBack();

        _showing.Should().Be(AppScreen.RawMaterials);
        _history.CanGoForward.Should().BeTrue();
    }

    [Test]
    public Task Back_goes_nowhere_until_the_question_is_answered() => StaThread.RunAsync(async () =>
    {
        Show(AppScreen.Products);
        _products.HasUnsavedChanges = true;
        _products.HoldAnswer = new TaskCompletionSource();
        _products.Answer = _ =>
        {
            _products.HasUnsavedChanges = false;
            return true;
        };

        _navigator.GoBack().Should().BeTrue("it comes back at once, with the question still up");
        _showing.Should().Be(AppScreen.Products);

        _products.HoldAnswer.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_showing != AppScreen.RawMaterials && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current!.Execution.CancellationToken);
        }

        _showing.Should().Be(AppScreen.RawMaterials);
    });

    [Test]
    public void Back_whose_question_fails_says_so_and_stays()
    {
        var failure = new InvalidOperationException("The question couldn't be asked.");
        Show(AppScreen.Products);
        _products.HasUnsavedChanges = true;
        _products.Answer = _ => throw failure;

        _navigator.GoBack();

        _failures.Should().Equal(failure);
        _showing.Should().Be(AppScreen.Products);
        _history.CanGoBack.Should().BeTrue();
    }

    // A tab picked, which MainWindow has already turned back

    [Test]
    public void A_tab_picked_with_unsaved_changes_is_told_which_form_it_would_leave()
    {
        _rawMaterials.HasUnsavedChanges = true;

        _navigator.UnsavedChangesLeft(AppScreen.RawMaterials, AppScreen.Products).Should().BeSameAs(_rawMaterials);
    }

    [Test]
    [Arguments(AppScreen.Products, AppScreen.Print, true)]
    [Arguments(AppScreen.Print, AppScreen.Products, true)]
    [Arguments(AppScreen.Products, AppScreen.RawMaterials, false)]
    [Arguments(AppScreen.Reports, AppScreen.Products, false)]
    public void A_tab_picked_leaves_nothing_on_the_same_form_or_with_nothing_unsaved(AppScreen from, AppScreen to, bool isUnsaved)
    {
        _products.HasUnsavedChanges = isUnsaved;

        _navigator.UnsavedChangesLeft(from, to).Should().BeNull();
    }

    [Test]
    public Task After_asking_goes_on_once_the_form_is_answered_putting_it_back_on_no() => StaThread.RunAsync(async () =>
    {
        _rawMaterials.HasUnsavedChanges = true;

        await _navigator.AfterAskingAsync(_rawMaterials, () => _events.Add("moved"));

        _events.Should().Equal("asked Raw Materials, revert True", "moved");
    });

    [Test]
    public Task After_asking_goes_nowhere_on_a_cancel() => StaThread.RunAsync(async () =>
    {
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => false;

        await _navigator.AfterAskingAsync(_rawMaterials, () => _events.Add("moved"));

        _events.Should().NotContain("moved");
    });

    [Test]
    public Task After_asking_says_so_when_what_follows_fails() => StaThread.RunAsync(async () =>
    {
        var failure = new InvalidOperationException("The tab couldn't be shown.");
        _rawMaterials.HasUnsavedChanges = true;

        await _navigator.AfterAskingAsync(_rawMaterials, () => throw failure);

        _failures.Should().Equal(failure);
    });

    // Closing the app

    [Test]
    public void Closing_only_asks_when_something_is_unsaved()
    {
        _navigator.HasUnsavedChanges.Should().BeFalse();

        _products.HasUnsavedChanges = true;

        _navigator.HasUnsavedChanges.Should().BeTrue();
    }

    [Test]
    public Task Closing_with_nothing_unsaved_closes_without_a_question() => StaThread.RunAsync(async () =>
    {
        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _events.Should().Equal("closed");
    });

    [Test]
    public Task Closing_asks_the_screen_showing_first_and_shows_each_as_its_asked_about() => StaThread.RunAsync(async () =>
    {
        _showing = AppScreen.Products;
        _products.HasUnsavedChanges = true;
        _rawMaterials.HasUnsavedChanges = true;
        _products.Answer = _ =>
        {
            _products.HasUnsavedChanges = false;
            return true;
        };
        _rawMaterials.Answer = _ =>
        {
            _rawMaterials.HasUnsavedChanges = false;
            return true;
        };

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _events.Should().Equal("asked Products, revert False", "showed RawMaterials", "asked Raw Materials, revert False", "closed");
    });

    // No leaves a form unsaved, for the app to close over. Found in the real app: No to the first of two asked about the
    // second with the first still showing, then about the first again.
    [Test]
    public Task Closing_asks_each_form_once_when_no_leaves_it_unsaved() => StaThread.RunAsync(async () =>
    {
        _showing = AppScreen.Products;
        _products.HasUnsavedChanges = true;
        _rawMaterials.HasUnsavedChanges = true;

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _events.Should().Equal("asked Products, revert False", "showed RawMaterials", "asked Raw Materials, revert False", "closed");
        _products.Questions.Should().Be(1);
    });

    [Test]
    public Task Closing_asks_about_a_form_two_screens_share_once() => StaThread.RunAsync(async () =>
    {
        _showing = AppScreen.Print;
        _products.HasUnsavedChanges = true;

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _products.Questions.Should().Be(1);
        _events.Should().Equal("asked Products, revert False", "closed");
    });

    [Test]
    public Task Closing_cancelled_stays_open_and_asks_nothing_further() => StaThread.RunAsync(async () =>
    {
        _rawMaterials.HasUnsavedChanges = true;
        _products.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => false;

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _events.Should().Equal("asked Raw Materials, revert False");
    });

    [Test]
    public Task Closing_cancelled_asks_again_the_next_time_and_when_a_tab_is_picked() => StaThread.RunAsync(async () =>
    {
        _showing = AppScreen.Products;
        _products.HasUnsavedChanges = true;
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => false;

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _navigator.UnsavedChangesLeft(AppScreen.Products, AppScreen.Reports).Should().BeSameAs(_products, "No only held for that close, which stopped");
    });

    [Test]
    public Task Closing_that_fails_says_so_and_stays_open() => StaThread.RunAsync(async () =>
    {
        var failure = new InvalidOperationException("The question couldn't be asked.");
        _rawMaterials.HasUnsavedChanges = true;
        _rawMaterials.Answer = _ => throw failure;

        await _navigator.CloseAfterAskingAsync(() => _events.Add("closed"));

        _failures.Should().Equal(failure);
        _events.Should().NotContain("closed");
    });

    [Test]
    public Task Closing_says_so_when_the_close_itself_fails() => StaThread.RunAsync(async () =>
    {
        var failure = new InvalidOperationException("The window wouldn't close.");

        await _navigator.CloseAfterAskingAsync(() => throw failure);

        _failures.Should().Equal(failure);
    });

    /// <summary>A form that answers its question as the test says: leaving it as it is, unless the test says otherwise.</summary>
    private sealed class FakeForm(string name) : IUnsavedChangesScreen
    {
        public string Name { get; } = name;

        public bool HasUnsavedChanges { get; set; }

        public int Questions { get; private set; }

        public Action<bool>? Asked { get; set; }

        public Func<bool, bool> Answer { get; set; } = _ => true;

        /// <summary>Set to hold the answer back until the test completes it, as someone reading the question does.</summary>
        public TaskCompletionSource? HoldAnswer { get; set; }

        public async Task<bool> ResolveUnsavedChangesAsync(bool revert)
        {
            Questions++;
            Asked?.Invoke(revert);
            if (HoldAnswer is { } hold)
            {
                await hold.Task;
            }

            return Answer(revert);
        }
    }
}
