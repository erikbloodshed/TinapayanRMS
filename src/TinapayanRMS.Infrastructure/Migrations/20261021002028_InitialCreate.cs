using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TinapayanRMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:case_insensitive", "und-u-ks-level2,und-u-ks-level2,icu,False");

            migrationBuilder.CreateTable(
                name: "costing_defaults",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    labor_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    overhead_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    administrative_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    shrinkage_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_buffer_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 10m),
                    new_product_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Production")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_defaults", x => x.id);
                    table.CheckConstraint("CK_CostingDefaults_CostBufferPercent", "cost_buffer_percent >= 0 AND cost_buffer_percent <= 100");
                    table.CheckConstraint("CK_CostingDefaults_ShrinkagePercent", "shrinkage_percent >= 0 AND shrinkage_percent < 100");
                    table.CheckConstraint("CK_CostingDefaults_SingleRow", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "raw_material_categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Ingredient")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_material_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sub_assembly_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_packaging = table.Column<bool>(type: "boolean", nullable: false),
                    is_product_owned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_assembly_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    abbreviation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, collation: "case_insensitive")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units_of_measure", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "case_insensitive"),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Production"),
                    is_composite = table.Column<bool>(type: "boolean", nullable: false),
                    includes_component_packaging = table.Column<bool>(type: "boolean", nullable: false),
                    quantity_required = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    current_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("CK_Products_ComponentPackagingOnComposite", "is_composite OR NOT includes_component_packaging");
                    table.CheckConstraint("CK_Products_CompositeIsOneUnit", "NOT is_composite OR quantity_required = 1");
                    table.CheckConstraint("CK_Products_QuantityRequired", "quantity_required > 0");
                    table.ForeignKey(
                        name: "fk_products_product_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "product_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "raw_materials",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    weight_loss_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    linked_to_id = table.Column<int>(type: "integer", nullable: true),
                    weight_share_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    cost_share_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    base_unit_id = table.Column<int>(type: "integer", nullable: true),
                    cost_per_base_unit = table.Column<decimal>(type: "numeric(28,20)", precision: 28, scale: 20, nullable: false),
                    cost_buffer_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    buffered_cost_per_base_unit = table.Column<decimal>(type: "numeric(28,20)", precision: 28, scale: 20, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cost_updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_materials", x => x.id);
                    table.CheckConstraint("CK_RawMaterials_BaseUnitMatchesType", "(type = 'Ingredient' AND base_unit_id IS NULL) OR (type = 'Packaging' AND base_unit_id IS NOT NULL)");
                    table.CheckConstraint("CK_RawMaterials_CostBufferPercent", "cost_buffer_percent >= 0 AND cost_buffer_percent <= 100");
                    table.CheckConstraint("CK_RawMaterials_LinkMatchesSource", "(source = 'Linked' AND linked_to_id IS NOT NULL AND weight_share_percent > 0 AND weight_share_percent <= 100 AND cost_share_percent > 0 AND cost_share_percent <= 100) OR (source <> 'Linked' AND linked_to_id IS NULL AND weight_share_percent IS NULL AND cost_share_percent IS NULL)");
                    table.CheckConstraint("CK_RawMaterials_OnlyIngredientsAreMade", "type = 'Ingredient' OR source = 'Purchased'");
                    table.ForeignKey(
                        name: "fk_raw_materials_raw_material_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "raw_material_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_raw_materials_raw_materials_linked_to_id",
                        column: x => x.linked_to_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_raw_materials_units_of_measure_base_unit_id",
                        column: x => x.base_unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "costing_parameters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    labor_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    overhead_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    administrative_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_parameters", x => x.id);
                    table.ForeignKey(
                        name: "fk_costing_parameters_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_components",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    component_product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_components", x => x.id);
                    table.CheckConstraint("CK_ProductComponents_NotItself", "product_id <> component_product_id");
                    table.CheckConstraint("CK_ProductComponents_Quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_product_components_products_component_product_id",
                        column: x => x.component_product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_components_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "case_insensitive"),
                    content = table.Column<string>(type: "text", nullable: false, collation: "case_insensitive")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_versions", x => x.id);
                    table.CheckConstraint("CK_ProductVersions_Number", "number >= 1");
                    table.ForeignKey(
                        name: "fk_product_versions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sub_assemblies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    owner_product_id = table.Column<int>(type: "integer", nullable: true),
                    total_weight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_assemblies", x => x.id);
                    table.ForeignKey(
                        name: "fk_sub_assemblies_products_owner_product_id",
                        column: x => x.owner_product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sub_assemblies_sub_assembly_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "sub_assembly_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingredient_bom_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ingredient_id = table.Column<int>(type: "integer", nullable: false),
                    component_ingredient_id = table.Column<int>(type: "integer", nullable: false),
                    grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient_bom_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingredient_bom_lines_raw_materials_component_ingredient_id",
                        column: x => x.component_ingredient_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ingredient_bom_lines_raw_materials_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_material_units",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_material_id = table.Column<int>(type: "integer", nullable: false),
                    unit_of_measure_id = table.Column<int>(type: "integer", nullable: false),
                    base_units_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "case_insensitive")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_material_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_raw_material_units_raw_materials_raw_material_id",
                        column: x => x.raw_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_raw_material_units_units_of_measure_unit_of_measure_id",
                        column: x => x.unit_of_measure_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "packaging_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    linked_product_id = table.Column<int>(type: "integer", nullable: true),
                    linked_sub_assembly_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packaging_links", x => x.id);
                    table.CheckConstraint("CK_PackagingLinks_OneTarget", "(linked_product_id IS NOT NULL AND linked_sub_assembly_id IS NULL) OR (linked_product_id IS NULL AND linked_sub_assembly_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_packaging_links_products_linked_product_id",
                        column: x => x.linked_product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packaging_links_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_packaging_links_sub_assemblies_linked_sub_assembly_id",
                        column: x => x.linked_sub_assembly_id,
                        principalTable: "sub_assemblies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_sub_assembly_usages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    sub_assembly_id = table.Column<int>(type: "integer", nullable: false),
                    authored_field = table.Column<int>(type: "integer", nullable: false),
                    authored_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    shrinkage_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_sub_assembly_usages", x => x.id);
                    table.CheckConstraint("CK_ProductSubAssemblyUsages_AuthoredValue", "authored_value >= 0");
                    table.CheckConstraint("CK_ProductSubAssemblyUsages_ShrinkagePercent", "shrinkage_percent >= 0 AND shrinkage_percent < 100");
                    table.ForeignKey(
                        name: "fk_product_sub_assembly_usages_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_sub_assembly_usages_sub_assemblies_sub_assembly_id",
                        column: x => x.sub_assembly_id,
                        principalTable: "sub_assemblies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sub_assembly_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sub_assembly_id = table.Column<int>(type: "integer", nullable: false),
                    ingredient_id = table.Column<int>(type: "integer", nullable: false),
                    grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_formula_base = table.Column<bool>(type: "boolean", nullable: false),
                    authored_bakers_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_assembly_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_sub_assembly_lines_raw_materials_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sub_assembly_lines_sub_assemblies_sub_assembly_id",
                        column: x => x.sub_assembly_id,
                        principalTable: "sub_assemblies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "packaging_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    packaging_material_id = table.Column<int>(type: "integer", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    qty_per_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    units_per_packaging_unit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    authored_field = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packaging_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_packaging_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_packaging_lines_raw_material_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "raw_material_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_packaging_lines_raw_materials_packaging_material_id",
                        column: x => x.packaging_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raw_material_brand_prices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_material_id = table.Column<int>(type: "integer", nullable: false),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "case_insensitive"),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_material_brand_prices", x => x.id);
                    table.CheckConstraint("CK_RawMaterialBrandPrices_Price", "price > 0");
                    table.ForeignKey(
                        name: "fk_raw_material_brand_prices_raw_material_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "raw_material_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_raw_material_brand_prices_raw_materials_raw_material_id",
                        column: x => x.raw_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_material_price_histories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_material_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "case_insensitive"),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_material_price_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_raw_material_price_histories_raw_material_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "raw_material_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_raw_material_price_histories_raw_materials_raw_material_id",
                        column: x => x.raw_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sub_assembly_packaging_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sub_assembly_id = table.Column<int>(type: "integer", nullable: false),
                    packaging_material_id = table.Column<int>(type: "integer", nullable: false),
                    unit_id = table.Column<int>(type: "integer", nullable: true),
                    qty_per_set = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_assembly_packaging_lines", x => x.id);
                    table.CheckConstraint("CK_SubAssemblyPackagingLines_QtyPerSet", "qty_per_set > 0");
                    table.ForeignKey(
                        name: "fk_sub_assembly_packaging_lines_raw_material_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "raw_material_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sub_assembly_packaging_lines_raw_materials_packaging_materi",
                        column: x => x.packaging_material_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sub_assembly_packaging_lines_sub_assemblies_sub_assembly_id",
                        column: x => x.sub_assembly_id,
                        principalTable: "sub_assemblies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_add_in_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usage_id = table.Column<int>(type: "integer", nullable: false),
                    ingredient_id = table.Column<int>(type: "integer", nullable: false),
                    grams = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_add_in_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_add_in_lines_product_sub_assembly_usages_usage_id",
                        column: x => x.usage_id,
                        principalTable: "product_sub_assembly_usages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usage_add_in_lines_raw_materials_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "raw_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "sub_assembly_roles",
                columns: new[] { "id", "is_packaging", "is_product_owned", "name", "sort_order" },
                values: new object[,]
                {
                    { 1, false, false, "Dough", 1 },
                    { 2, false, false, "Filling", 2 },
                    { 3, false, false, "Topping", 3 },
                    { 4, false, false, "Inclusion", 4 },
                    { 5, false, false, "Finishing", 5 },
                    { 6, true, false, "Packaging", 6 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_costing_parameters_product_id",
                table: "costing_parameters",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_bom_lines_component_ingredient_id",
                table: "ingredient_bom_lines",
                column: "component_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_bom_lines_ingredient_id_component_ingredient_id",
                table: "ingredient_bom_lines",
                columns: new[] { "ingredient_id", "component_ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packaging_lines_packaging_material_id",
                table: "packaging_lines",
                column: "packaging_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_lines_product_id_packaging_material_id",
                table: "packaging_lines",
                columns: new[] { "product_id", "packaging_material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_packaging_lines_unit_id",
                table: "packaging_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_links_linked_product_id",
                table: "packaging_links",
                column: "linked_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_links_linked_sub_assembly_id",
                table: "packaging_links",
                column: "linked_sub_assembly_id");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_links_product_id",
                table: "packaging_links",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_categories_name",
                table: "product_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_components_component_product_id",
                table: "product_components",
                column: "component_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_components_product_id_component_product_id",
                table: "product_components",
                columns: new[] { "product_id", "component_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_sub_assembly_usages_product_id_sub_assembly_id",
                table: "product_sub_assembly_usages",
                columns: new[] { "product_id", "sub_assembly_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_sub_assembly_usages_sub_assembly_id",
                table: "product_sub_assembly_usages",
                column: "sub_assembly_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_versions_product_id_number",
                table: "product_versions",
                columns: new[] { "product_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_brand_prices_raw_material_id",
                table: "raw_material_brand_prices",
                column: "raw_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_brand_prices_unit_id",
                table: "raw_material_brand_prices",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_categories_name",
                table: "raw_material_categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_price_histories_raw_material_id_date",
                table: "raw_material_price_histories",
                columns: new[] { "raw_material_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_price_histories_unit_id",
                table: "raw_material_price_histories",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_units_raw_material_id_unit_of_measure_id",
                table: "raw_material_units",
                columns: new[] { "raw_material_id", "unit_of_measure_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_material_units_unit_of_measure_id",
                table: "raw_material_units",
                column: "unit_of_measure_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_materials_base_unit_id",
                table: "raw_materials",
                column: "base_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_materials_category_id",
                table: "raw_materials",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_materials_linked_to_id",
                table: "raw_materials",
                column: "linked_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_materials_name",
                table: "raw_materials",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_assemblies_name",
                table: "sub_assemblies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_assemblies_owner_product_id",
                table: "sub_assemblies",
                column: "owner_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_assemblies_role_id",
                table: "sub_assemblies",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_lines_ingredient_id",
                table: "sub_assembly_lines",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_lines_sub_assembly_id_ingredient_id",
                table: "sub_assembly_lines",
                columns: new[] { "sub_assembly_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_packaging_lines_packaging_material_id",
                table: "sub_assembly_packaging_lines",
                column: "packaging_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_packaging_lines_sub_assembly_id_packaging_mate",
                table: "sub_assembly_packaging_lines",
                columns: new[] { "sub_assembly_id", "packaging_material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_packaging_lines_unit_id",
                table: "sub_assembly_packaging_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_assembly_roles_name",
                table: "sub_assembly_roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_abbreviation",
                table: "units_of_measure",
                column: "abbreviation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_name",
                table: "units_of_measure",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_add_in_lines_ingredient_id",
                table: "usage_add_in_lines",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_add_in_lines_usage_id_ingredient_id",
                table: "usage_add_in_lines",
                columns: new[] { "usage_id", "ingredient_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "costing_defaults");

            migrationBuilder.DropTable(
                name: "costing_parameters");

            migrationBuilder.DropTable(
                name: "ingredient_bom_lines");

            migrationBuilder.DropTable(
                name: "packaging_lines");

            migrationBuilder.DropTable(
                name: "packaging_links");

            migrationBuilder.DropTable(
                name: "product_components");

            migrationBuilder.DropTable(
                name: "product_versions");

            migrationBuilder.DropTable(
                name: "raw_material_brand_prices");

            migrationBuilder.DropTable(
                name: "raw_material_price_histories");

            migrationBuilder.DropTable(
                name: "sub_assembly_lines");

            migrationBuilder.DropTable(
                name: "sub_assembly_packaging_lines");

            migrationBuilder.DropTable(
                name: "usage_add_in_lines");

            migrationBuilder.DropTable(
                name: "raw_material_units");

            migrationBuilder.DropTable(
                name: "product_sub_assembly_usages");

            migrationBuilder.DropTable(
                name: "raw_materials");

            migrationBuilder.DropTable(
                name: "sub_assemblies");

            migrationBuilder.DropTable(
                name: "raw_material_categories");

            migrationBuilder.DropTable(
                name: "units_of_measure");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "sub_assembly_roles");

            migrationBuilder.DropTable(
                name: "product_categories");
        }
    }
}
