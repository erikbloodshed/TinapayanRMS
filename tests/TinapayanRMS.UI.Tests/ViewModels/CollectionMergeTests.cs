using System.Collections.ObjectModel;
using AwesomeAssertions;
using DynamicData;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

public class CollectionMergeTests
{
    [Test]
    public void HasSameValues_is_true_for_two_copies_of_the_same_row()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 930m, "bag")).Should().BeTrue();
    }

    [Test]
    public void HasSameValues_detects_a_changed_value()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 950m, "bag")).Should().BeFalse();
    }

    [Test]
    public void HasSameValues_detects_a_change_in_a_referenced_row()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 930m, "sack")).Should().BeFalse();
    }

    [Test]
    public void HasSameValues_is_true_when_an_owned_collection_holds_the_same_values()
    {
        var a = Row(1, "Flour", 930m, "bag");
        a.Units.Add(OwnedUnit(9, "pack", 1000m));
        var b = Row(1, "Flour", 930m, "bag");
        b.Units.Add(OwnedUnit(9, "pack", 1000m));

        CollectionMerge.HasSameValues(a, b).Should().BeTrue();
    }

    [Test]
    public void HasSameValues_detects_an_added_element_in_an_owned_collection()
    {
        var withPack = Row(1, "Flour", 930m, "bag");
        withPack.Units.Add(OwnedUnit(9, "pack", 1000m));

        CollectionMerge.HasSameValues(withPack, Row(1, "Flour", 930m, "bag")).Should().BeFalse();
    }

    [Test]
    public void HasSameValues_detects_a_change_inside_an_owned_collection_element_and_what_it_references()
    {
        var a = Row(1, "Flour", 930m, "bag");
        a.Units.Add(OwnedUnit(9, "pack", 1000m));
        var renamed = Row(1, "Flour", 930m, "bag");
        renamed.Units.Add(OwnedUnit(9, "packet", 1000m));
        var resized = Row(1, "Flour", 930m, "bag");
        resized.Units.Add(OwnedUnit(9, "pack", 500m));

        CollectionMerge.HasSameValues(a, renamed).Should().BeFalse();
        CollectionMerge.HasSameValues(a, resized).Should().BeFalse();
    }

    [Test]
    public void Merging_into_a_cache_keeps_unchanged_rows_replaces_changed_ones_and_adds_and_removes_the_rest()
    {
        var cache = new SourceCache<TestRow, int>(r => r.Id);
        var flour = Row(1, "Flour", 930m, "bag");
        var sugar = Row(2, "Sugar", 80m, "kg");
        cache.AddOrUpdate([flour, sugar, Row(3, "Salt", 20m, "kg")]);

        var repricedSugar = Row(2, "Sugar", 85m, "kg");
        var butter = Row(4, "Butter", 13500m, "block");
        cache.MergeFrom([Row(1, "Flour", 930m, "bag"), repricedSugar, butter]);

        cache.Lookup(1).Value.Should().BeSameAs(flour);
        cache.Lookup(2).Value.Should().BeSameAs(repricedSugar);
        cache.Lookup(3).HasValue.Should().BeFalse();
        cache.Lookup(4).Value.Should().BeSameAs(butter);
    }

    [Test]
    public void Merging_into_a_list_follows_the_fresh_order_and_keeps_unchanged_rows()
    {
        var flour = Row(1, "Flour", 930m, "bag");
        var salt = Row(3, "Salt", 20m, "kg");
        var list = new ObservableCollection<TestRow> { flour, Row(2, "Sugar", 80m, "kg"), salt };

        var repricedSugar = Row(2, "Sugar", 85m, "kg");
        var butter = Row(4, "Butter", 13500m, "block");
        list.MergeFrom([butter, Row(3, "Salt", 20m, "kg"), repricedSugar, Row(1, "Flour", 930m, "bag")], r => r.Id);

        list.Select(r => r.Id).Should().Equal(4, 3, 2, 1);
        list[0].Should().BeSameAs(butter);
        list[1].Should().BeSameAs(salt);
        list[2].Should().BeSameAs(repricedSugar);
        list[3].Should().BeSameAs(flour);
    }

    [Test]
    public void Merging_into_a_list_removes_rows_that_are_gone()
    {
        var list = new ObservableCollection<TestRow> { Row(1, "Flour", 930m, "bag"), Row(2, "Sugar", 80m, "kg") };

        list.MergeFrom([Row(2, "Sugar", 80m, "kg")], r => r.Id);

        list.Select(r => r.Id).Should().Equal(2);
    }

    [Test]
    public void Merging_by_a_key_rows_share_matches_them_in_order()
    {
        var firstDough = Row(1, "Dough", 10m, "g");
        var secondDough = Row(2, "Dough", 20m, "g");
        var list = new ObservableCollection<TestRow> { firstDough, secondDough };

        var repricedSecond = Row(2, "Dough", 25m, "g");
        list.MergeFrom([Row(1, "Dough", 10m, "g"), repricedSecond, Row(3, "Filling", 5m, "g")], r => r.Name);

        list.Should().HaveCount(3);
        list[0].Should().BeSameAs(firstDough);
        list[1].Should().BeSameAs(repricedSecond);
        list[2].Name.Should().Be("Filling");
    }

    private static TestRow Row(int id, string name, decimal cost, string unitName) =>
        new() { Id = id, Name = name, Cost = cost, Unit = new TestUnit { Id = 100, Name = unitName } };

    private static TestOwnedUnit OwnedUnit(int id, string unitName, decimal multiplier) =>
        new() { Id = id, Multiplier = multiplier, Unit = new TestUnit { Id = 200, Name = unitName } };

    public sealed class TestRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Cost { get; set; }

        public TestUnit? Unit { get; set; }

        public List<TestOwnedUnit> Units { get; } = [];
    }

    public sealed class TestUnit
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Shaped like RawMaterialUnit: an owned row that references a unit of measure.</summary>
    public sealed class TestOwnedUnit
    {
        public int Id { get; set; }

        public decimal Multiplier { get; set; }

        public TestUnit? Unit { get; set; }
    }
}
