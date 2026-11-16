using AwesomeAssertions;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.Trees;

/// <summary>
/// The rows picked in a screen's list on their own, with strings standing in for the tree's rows: the contract the three
/// screens' trees share, rather than any screen's use of it (their *StateTreeTests).
/// </summary>
public class TreeSelectionTests
{
    [Test]
    public void Pick_SeveralPicked_LeavesThatRowAlone()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        selection.Nodes.Add("Sugar");

        selection.Pick("Butter");

        selection.Nodes.Should().Equal("Butter");
    }

    [Test]
    public void Pick_Null_PicksNothing()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");

        selection.Pick(null);

        selection.Nodes.Should().BeEmpty();
    }

    [Test]
    public void Pick_TheRowAlreadyPickedAlone_ChangesNothing()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        var changes = 0;
        selection.Nodes.CollectionChanged += (_, _) => changes++;

        selection.Pick("Flour");

        changes.Should().Be(0, "the tree is mid-change when a click's pick comes back to it");
    }

    [Test]
    public void BringFirst_ARowPickedWithOthers_MovesItFirstAndKeepsTheOthers()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        selection.Nodes.Add("Sugar");
        selection.Nodes.Add("Butter");

        selection.BringFirst("Butter");

        selection.Nodes.Should().Equal("Butter", "Flour", "Sugar");
    }

    [Test]
    public void BringFirst_ARowNotPicked_PicksItAlone()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        selection.Nodes.Add("Sugar");

        selection.BringFirst("Butter");

        selection.Nodes.Should().Equal("Butter");
    }

    [Test]
    public void BringFirst_Null_PicksNothing()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");

        selection.BringFirst(null);

        selection.Nodes.Should().BeEmpty();
    }

    [Test]
    public void BringFirst_ARowAlreadyFirst_ChangesNothing()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        selection.Nodes.Add("Sugar");
        var changes = 0;
        selection.Nodes.CollectionChanged += (_, _) => changes++;

        selection.BringFirst("Flour");

        changes.Should().Be(0);
        selection.Nodes.Should().Equal("Flour", "Sugar");
    }

    [Test]
    public void BringFirst_WhileChanging_IsSyncing()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Flour");
        var syncingAtEachChange = new List<bool>();
        selection.Nodes.CollectionChanged += (_, _) => syncingAtEachChange.Add(selection.IsSyncing);

        selection.BringFirst("Sugar");

        syncingAtEachChange.Should().NotBeEmpty().And.AllSatisfy(syncing => syncing.Should().BeTrue());
        selection.IsSyncing.Should().BeFalse();
    }

    [Test]
    public void Restore_PicksAgainOnlyTheRowsStillListedAndNotPicked()
    {
        var selection = new TreeSelection();
        selection.Nodes.Add("Sugar");

        selection.Restore(["Flour", "Sugar", "Gone"], row => !Equals(row, "Gone"));

        selection.Nodes.Should().Equal("Sugar", "Flour");
    }

    [Test]
    public void Restore_WhileChanging_IsSyncing()
    {
        var selection = new TreeSelection();
        var syncingAtEachChange = new List<bool>();
        selection.Nodes.CollectionChanged += (_, _) => syncingAtEachChange.Add(selection.IsSyncing);

        selection.Restore(["Flour", "Sugar"], _ => true);

        syncingAtEachChange.Should().HaveCount(2).And.AllSatisfy(syncing => syncing.Should().BeTrue());
        selection.IsSyncing.Should().BeFalse();
    }

    [Test]
    public void Quietly_Nested_StaysSyncingUntilTheOuterChangeEnds()
    {
        var selection = new TreeSelection();
        var syncingAfterInner = false;

        selection.Quietly(() =>
        {
            selection.Quietly(() => { });
            syncingAfterInner = selection.IsSyncing;
        });

        syncingAfterInner.Should().BeTrue();
        selection.IsSyncing.Should().BeFalse();
    }

    [Test]
    public void Quietly_AChangeThatThrows_StopsSyncing()
    {
        var selection = new TreeSelection();

        var act = () => selection.Quietly(() => throw new InvalidOperationException());

        act.Should().Throw<InvalidOperationException>();
        selection.IsSyncing.Should().BeFalse();
    }
}
