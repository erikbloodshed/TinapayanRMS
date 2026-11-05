using AwesomeAssertions;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Pricing.Tests.Versions;

/// <summary>What a save adds to its message for the versions it recorded of other products.</summary>
public class ProductVersionRecorderTests
{
    [Fact]
    public void The_message_names_up_to_three_products_and_counts_the_rest()
    {
        ProductVersionRecorder.RecordedMessage([]).Should().BeNull();
        ProductVersionRecorder.RecordedMessage(["Pandesal"]).Should().Be("A version was recorded for Pandesal.");
        ProductVersionRecorder.RecordedMessage(["Pandesal", "Pandesal x 8"])
            .Should().Be("A version was recorded for Pandesal and Pandesal x 8.");
        ProductVersionRecorder.RecordedMessage(["Asado Roll", "Pandesal", "Tuna Roll"])
            .Should().Be("A version was recorded for Asado Roll, Pandesal and Tuna Roll.");
        ProductVersionRecorder.RecordedMessage(["Asado Roll", "Cheese Roll", "Pandesal", "Tuna Roll", "Ube Loaf"])
            .Should().Be("A version was recorded for Asado Roll, Cheese Roll, Pandesal and 2 more.");
    }
}
