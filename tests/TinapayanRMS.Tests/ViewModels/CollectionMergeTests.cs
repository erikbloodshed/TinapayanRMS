using System.Collections.ObjectModel;
using AwesomeAssertions;
using DynamicData;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

public class CollectionMergeTests
{
    [Fact]
    public void HasSameValues_is_true_for_two_copies_of_the_same_row()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 930m, "bag")).Should().BeTrue();
    }

    [Fact]
    public void HasSameValues_detects_a_changed_value()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 950m, "bag")).Should().BeFalse();
    }

    [Fact]
    public void HasSameValues_detects_a_change_in_a_referenced_row()
    {
        CollectionMerge.HasSameValues(Row(1, "Flour", 930m, "bag"), Row(1, "Flour", 930m, "sack")).Should().BeFalse();
    }

    [Fact]
    public void HasSameValues_ignores_collections()
    {
        var withHistory = Row(1, "Flour", 930m, "bag");
        withHistory.History.Add(new TestUnit { Id = 9, Name = "old" });

        CollectionMerge.HasSameValues(withHistory, Row(1, "Flour", 930m, "bag")).Should().BeTrue();
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public void Merging_into_a_list_removes_rows_that_are_gone()
    {
        var list = new ObservableCollection<TestRow> { Row(1, "Flour", 930m, "bag"), Row(2, "Sugar", 80m, "kg") };

        list.MergeFrom([Row(2, "Sugar", 80m, "kg")], r => r.Id);

        list.Select(r => r.Id).Should().Equal(2);
    }

    private static TestRow Row(int id, string name, decimal cost, string unitName) =>
        new() { Id = id, Name = name, Cost = cost, Unit = new TestUnit { Id = 100, Name = unitName } };

    public sealed class TestRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Cost { get; set; }

        public TestUnit? Unit { get; set; }

        public List<TestUnit> History { get; } = [];
    }

    public sealed class TestUnit
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
