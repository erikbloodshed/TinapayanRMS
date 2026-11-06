using AwesomeAssertions;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>What a row's grid and the screen's WhenAnyValue and AutoRefresh depend on, as ReactiveObject gave them.</summary>
public class ObservableRowTests
{
    private readonly Row _row = new();
    private readonly List<string?> _changed = [];

    public ObservableRowTests() => _row.PropertyChanged += (_, e) => _changed.Add(e.PropertyName);

    [Test]
    public void A_new_value_is_kept_and_said_by_its_property_name()
    {
        _row.Quantity = 2;

        _row.Quantity.Should().Be(2);
        _changed.Should().Equal(nameof(Row.Quantity));
    }

    [Test]
    public void The_value_it_holds_already_says_nothing()
    {
        _row.Quantity = 2;
        _changed.Clear();

        _row.Quantity = 2;

        _changed.Should().BeEmpty();
    }

    [Test]
    public void What_follows_from_it_can_be_said_too()
    {
        _row.RaiseTotal();

        _changed.Should().Equal(nameof(Row.Total));
    }

    private sealed class Row : ObservableRow
    {
        private int _quantity;

        public int Quantity
        {
            get => _quantity;
            set => RaiseAndSetIfChanged(ref _quantity, value);
        }

        public int Total => Quantity * 2;

        public void RaiseTotal() => RaisePropertyChanged(nameof(Total));
    }
}
