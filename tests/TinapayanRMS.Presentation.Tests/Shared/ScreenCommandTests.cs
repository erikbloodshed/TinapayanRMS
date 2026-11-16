using System.Windows.Input;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Shared;

/// <summary>A screen's command: whether it can run, and running it, as a button, a menu or a key does.</summary>
public class ScreenCommandTests
{
    [Test]
    public void It_can_run_when_the_screen_says_so_and_asks_again_each_time()
    {
        var picked = false;
        var command = ScreenCommand.Of(() => { }, () => picked);

        command.CanExecute().Should().BeFalse();
        picked = true;
        command.CanExecute().Should().BeTrue("it's asked when it's wanted, not remembered");
        ((ICommand)command).CanExecute(null).Should().BeTrue();
    }

    [Test]
    public async Task It_runs_when_asked_to_whatever_it_says_about_running_as_a_load_does()
    {
        var ran = 0;
        var command = ScreenCommand.Of(() => ran++, () => false);

        await command.ExecuteAsync();

        ran.Should().Be(1, "asking first is the button's or the key's job (Commands.Run)");
    }

    [Test]
    public async Task It_cant_run_again_while_it_runs_and_says_so_as_it_starts_and_ends()
    {
        var release = new TaskCompletionSource();
        var command = new ScreenCommand(() => release.Task);
        var changes = new List<bool>();
        command.CanExecuteChanged += (_, _) => changes.Add(command.CanExecute());

        var running = command.ExecuteAsync();
        command.IsRunning.Should().BeTrue();
        command.CanExecute().Should().BeFalse("a second click while Save runs would save twice");

        release.SetResult();
        await running;

        command.IsRunning.Should().BeFalse();
        command.CanExecute().Should().BeTrue();
        changes.Should().Equal(false, true);
    }

    [Test]
    public async Task It_is_running_until_its_body_ends_however_it_ends()
    {
        var command = new ScreenCommand(() => Task.FromException(new InvalidOperationException("broken")));

        var run = () => command.ExecuteAsync();

        await run.Should().ThrowAsync<InvalidOperationException>("keeping it from throwing is the body's job (RunSafelyAsync)");
        command.CanExecute().Should().BeTrue();
    }

    [Test]
    public async Task One_that_takes_something_is_given_it_and_asked_about_it()
    {
        var opened = new List<int>();
        var command = ScreenCommand.Of<int>(opened.Add, id => id > 0);

        command.CanExecute(0).Should().BeFalse();
        command.CanExecute(42).Should().BeTrue();
        ((ICommand)command).CanExecute(42).Should().BeTrue();
        ((ICommand)command).CanExecute(null).Should().BeFalse("nothing is the default");
        await command.ExecuteAsync(42);

        opened.Should().Equal(42);
    }

    [Test]
    public void One_that_takes_something_turns_away_anything_else()
    {
        ICommand command = ScreenCommand.Of<int>(_ => { });

        var ask = () => command.CanExecute("42");

        ask.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public async Task Run_through_ICommand_it_runs_as_a_menu_item_does()
    {
        var ran = new TaskCompletionSource();
        ICommand command = ScreenCommand.Of(ran.SetResult);

        command.Execute(null);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
