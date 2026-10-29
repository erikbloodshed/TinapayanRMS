using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

public class SimulatedCostingTests
{
    private static ProductDetail Detail(CostingParameters? costing) => new(null, [], [], costing, [], []) { WastagePercent = 3m };

    [Fact]
    public void Each_change_goes_on_the_costing_and_points_never_take_a_percent_below_0()
    {
        var detail = Detail(new CostingParameters { LaborPercent = 30m, OverheadPercent = 4m, AdministrativePercent = 10m, MarginPercent = 50m, VatPercent = 12m });

        var tried = new SimulatedCosting(5m, 15m, 5m, -10m, null).Apply(detail);

        tried.WastagePercent.Should().Be(5m);
        (tried.Costing!.LaborPercent, tried.Costing.OverheadPercent, tried.Costing.AdministrativePercent, tried.Costing.MarginPercent, tried.Costing.VatPercent)
            .Should().Be((35m, 0m, 10m, 50m, 15m));
        detail.Costing!.LaborPercent.Should().Be(30m, "the product as read is left as it was");
    }

    [Fact]
    public void No_changes_leave_the_detail_as_it_is_and_a_product_without_a_costing_gets_one()
    {
        var detail = Detail(null);

        new SimulatedCosting(null, null, null, null, null).Apply(detail).Should().BeSameAs(detail);
        new SimulatedCosting(null, 12m, null, null, null).Apply(detail).Costing!.VatPercent.Should().Be(12m);
    }

    [Fact]
    public void A_wastage_or_VAT_out_of_range_is_refused_and_the_report_lists_each_change()
    {
        var wastage = () => new SimulatedCosting(101m, null, null, null, null).EnsureValid();
        wastage.Should().Throw<BusinessRuleException>();
        var vat = () => new SimulatedCosting(null, -1m, null, null, null).EnsureValid();
        vat.Should().Throw<BusinessRuleException>();

        new SimulatedCosting(5m, null, 2m, null, null).Printed(3m).Select(p => (p.Material, p.CurrentPrice, p.NewPrice))
            .Should().Equal(("Wastage % (shop-wide)", 3m, 5m), ("Labor % +2 points (every product)", null, 2m));
    }
}
