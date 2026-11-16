using System.Collections.Specialized;
using System.ComponentModel;
using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Shared;

/// <summary>
/// A table's rows replaced as one change: a grid drawn over the list draws all its rows again for each change it hears of,
/// and Reports' tables, filled a row at a time, held the window for seconds. Rows the same as before are no change at all.
/// </summary>
public class TableRowsTests
{
    private readonly TableRows<string> _table = ["Old row"];
    private readonly List<NotifyCollectionChangedAction> _changes = [];
    private readonly List<string?> _properties = [];

    public TableRowsTests()
    {
        _table.CollectionChanged += (_, e) => _changes.Add(e.Action);
        ((INotifyPropertyChanged)_table).PropertyChanged += (_, e) => _properties.Add(e.PropertyName);
    }

    [Test]
    public void Replacing_the_rows_is_one_change()
    {
        _table.ReplaceWith(["Ensaymada", "Pandesal", "Spanish Bread"]).Should().BeTrue();

        _table.Should().Equal("Ensaymada", "Pandesal", "Spanish Bread");
        _changes.Should().Equal([NotifyCollectionChangedAction.Reset], "the grid draws its rows once for the lot");
        _properties.Should().Equal("Count", "Item[]");
    }

    [Test]
    public void Rows_worked_out_from_the_table_itself_replace_it()
    {
        _table.ReplaceWith(["Ensaymada", "Pandesal", "Spanish Bread"]);

        _table.ReplaceWith(_table.Where(name => name != "Ensaymada"));

        _table.Should().Equal("Pandesal", "Spanish Bread");
    }

    [Test]
    public void No_rows_empty_it_in_one_change()
    {
        _table.ReplaceWith([]);

        _table.Should().BeEmpty();
        _changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    [Test]
    public void Rows_showing_the_same_as_before_change_nothing()
    {
        // Each load makes new rows: the grid would draw every one again for the same figures.
        TableRows<PriceRow> prices = [new("Flour", 60m), new("Salt", 5m)];
        var flour = prices[0];
        List<NotifyCollectionChangedAction> changes = [];
        prices.CollectionChanged += (_, e) => changes.Add(e.Action);

        prices.ReplaceWith([new("Flour", 60m), new("Salt", 5m)]).Should().BeFalse();

        changes.Should().BeEmpty();
        prices[0].Should().BeSameAs(flour, "it keeps its own rows");
    }

    [Test]
    public void One_new_figure_replaces_them_all()
    {
        TableRows<PriceRow> prices = [new("Flour", 60m), new("Salt", 5m)];
        List<NotifyCollectionChangedAction> changes = [];
        prices.CollectionChanged += (_, e) => changes.Add(e.Action);

        prices.ReplaceWith([new("Flour", 62m), new("Salt", 5m)]).Should().BeTrue();

        prices.Select(p => p.Price).Should().Equal(62m, 5m);
        changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    [Test]
    public void The_same_rows_in_another_order_replace_them()
    {
        _table.ReplaceWith(["Ensaymada", "Pandesal"]);
        _changes.Clear();

        _table.ReplaceWith(["Pandesal", "Ensaymada"]).Should().BeTrue();

        _table.Should().Equal("Pandesal", "Ensaymada");
        _changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    [Test]
    public void Text_rows_are_told_apart_by_their_text_not_its_length()
    {
        _table.ReplaceWith(["Ube"]);
        _changes.Clear();

        _table.ReplaceWith(["Yam"]).Should().BeTrue();

        _table.Should().Equal("Yam");
        _changes.Should().Equal(NotifyCollectionChangedAction.Reset);
    }

    /// <summary>A row as Reports' are: a class whose figures are its properties, made afresh by each load.</summary>
    private sealed class PriceRow(string name, decimal price)
    {
        public string Name => name;

        public decimal Price => price;
    }
}
