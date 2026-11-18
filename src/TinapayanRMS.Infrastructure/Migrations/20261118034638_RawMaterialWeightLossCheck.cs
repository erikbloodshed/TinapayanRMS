using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TinapayanRMS.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261118034638_RawMaterialWeightLossCheck : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "CK_RawMaterials_WeightLossPercent",
            table: "raw_materials",
            sql: "weight_loss_percent IS NULL OR (weight_loss_percent >= 0 AND weight_loss_percent < 100)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_RawMaterials_WeightLossPercent",
            table: "raw_materials");
    }
}
