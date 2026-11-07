using AwesomeAssertions;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The main window's Back and Forward (MainWindow.xaml.cs hands it visits): a step is a change of screen, and each
/// stop keeps the record its screen had open as it was left.
/// </summary>
public class NavigationHistoryTests
{
    public NavigationHistoryTests() => ReactiveUIBootstrapper.EnsureInitialized();

    [Test]
    public void Back_and_Forward_walk_the_screens_with_the_record_each_had_open_as_it_was_left()
    {
        var history = new NavigationHistory();
        history.Start(new ScreenVisit(AppScreen.Products, null));
        history.CanGoBack.Should().BeFalse();
        history.CanGoForward.Should().BeFalse();

        // Products (Ensaymada) → Sub-Assemblies (Glaze) → Products (Cream Loaf).
        history.Visit(new ScreenVisit(AppScreen.Products, 1), AppScreen.SubAssemblies);
        history.Visit(new ScreenVisit(AppScreen.SubAssemblies, 7), AppScreen.Products);
        history.CanGoBack.Should().BeTrue();

        history.GoBack(new ScreenVisit(AppScreen.Products, 2)).Should().Be(new ScreenVisit(AppScreen.SubAssemblies, 7));
        history.GoBack(new ScreenVisit(AppScreen.SubAssemblies, 7)).Should().Be(new ScreenVisit(AppScreen.Products, 1), "the product open when Products was first left");
        history.CanGoBack.Should().BeFalse();
        history.GoBack(new ScreenVisit(AppScreen.Products, 1)).Should().BeNull("there's nowhere further back");

        history.CanGoForward.Should().BeTrue();
        history.GoForward(new ScreenVisit(AppScreen.Products, 1)).Should().Be(new ScreenVisit(AppScreen.SubAssemblies, 7));
        history.GoForward(new ScreenVisit(AppScreen.SubAssemblies, 7)).Should().Be(new ScreenVisit(AppScreen.Products, 2), "the product open when Back left it");
        history.CanGoForward.Should().BeFalse();
        history.GoForward(new ScreenVisit(AppScreen.Products, 2)).Should().BeNull();
    }

    [Test]
    public void BackTarget_and_ForwardTarget_say_where_a_step_would_go_without_taking_it()
    {
        var history = new NavigationHistory();
        history.Start(new ScreenVisit(AppScreen.Products, null));
        history.BackTarget.Should().BeNull();
        history.ForwardTarget.Should().BeNull();

        history.Visit(new ScreenVisit(AppScreen.Products, 1), AppScreen.SubAssemblies);
        history.BackTarget.Should().Be(new ScreenVisit(AppScreen.Products, 1));
        history.BackTarget.Should().Be(new ScreenVisit(AppScreen.Products, 1), "asking twice goes nowhere");
        history.CanGoBack.Should().BeTrue();

        history.GoBack(new ScreenVisit(AppScreen.SubAssemblies, 7));
        history.BackTarget.Should().BeNull();
        history.ForwardTarget.Should().Be(new ScreenVisit(AppScreen.SubAssemblies, 7));
    }

    [Test]
    public void A_screen_changed_after_going_back_drops_what_Forward_held()
    {
        var history = new NavigationHistory();
        history.Start(new ScreenVisit(AppScreen.RawMaterials, null));
        history.Visit(new ScreenVisit(AppScreen.RawMaterials, 3), AppScreen.Products);
        history.GoBack(new ScreenVisit(AppScreen.Products, 1));
        history.CanGoForward.Should().BeTrue();

        history.Visit(new ScreenVisit(AppScreen.RawMaterials, 4), AppScreen.Reports);

        history.CanGoForward.Should().BeFalse("a new step after going back starts a new path, as a browser's link does");
        history.GoBack(new ScreenVisit(AppScreen.Reports, null)).Should().Be(new ScreenVisit(AppScreen.RawMaterials, 4));
    }

    [Test]
    public void A_move_to_the_screen_already_showing_is_not_a_step()
    {
        var history = new NavigationHistory();
        history.Start(new ScreenVisit(AppScreen.Products, null));

        history.Visit(new ScreenVisit(AppScreen.Products, 1), AppScreen.Products);

        history.CanGoBack.Should().BeFalse();
    }

    [Test]
    public void Back_keeps_only_the_latest_stops_past_its_depth()
    {
        var history = new NavigationHistory(depth: 2);
        history.Start(new ScreenVisit(AppScreen.RawMaterials, null));
        history.Visit(new ScreenVisit(AppScreen.RawMaterials, 1), AppScreen.SubAssemblies);
        history.Visit(new ScreenVisit(AppScreen.SubAssemblies, 2), AppScreen.Products);
        history.Visit(new ScreenVisit(AppScreen.Products, 3), AppScreen.Reports);

        history.GoBack(new ScreenVisit(AppScreen.Reports, null)).Should().Be(new ScreenVisit(AppScreen.Products, 3));
        history.GoBack(new ScreenVisit(AppScreen.Products, 3)).Should().Be(new ScreenVisit(AppScreen.SubAssemblies, 2));
        history.GoBack(new ScreenVisit(AppScreen.SubAssemblies, 2)).Should().BeNull("the oldest stop went when the third came");
    }

    [Test]
    public void CanGoBack_and_CanGoForward_are_raised_as_they_change()
    {
        var history = new NavigationHistory();
        history.Start(new ScreenVisit(AppScreen.Products, null));
        var raised = new List<string?>();
        history.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        history.Visit(new ScreenVisit(AppScreen.Products, 1), AppScreen.Reports);

        raised.Should().Contain([nameof(NavigationHistory.CanGoBack), nameof(NavigationHistory.CanGoForward)]);
    }
}
