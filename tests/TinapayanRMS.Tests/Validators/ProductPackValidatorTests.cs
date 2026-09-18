using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class ProductPackValidatorTests
{
    private readonly ProductPackValidator _validator = new();

    [Theory]
    [InlineData("Pack of 8", 8)] // a small bag of ensaymada
    [InlineData("Pack of 12", 12)] // the usual dozen
    [InlineData("Gift Box of 24", 24)] // a Christmas box
    public void Accepts_a_named_pack_holding_pieces(string name, int piecesPerPack)
    {
        _validator.Validate(new ProductPack { Name = name, PiecesPerPack = piecesPerPack }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_pack_with_no_name()
    {
        var result = _validator.Validate(new ProductPack { Name = string.Empty, PiecesPerPack = 12 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ProductPack.Name));
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_column_holds()
    {
        var result = _validator.Validate(
            new ProductPack { Name = new string('a', ProductPackValidator.MaxNameLength + 1), PiecesPerPack = 12 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ProductPack.Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_pack_that_holds_no_pieces(int piecesPerPack)
    {
        var result = _validator.Validate(new ProductPack { Name = "Pack of 12", PiecesPerPack = piecesPerPack });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ProductPack.PiecesPerPack));
    }
}
