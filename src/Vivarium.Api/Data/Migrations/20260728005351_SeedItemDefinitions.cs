using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedItemDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemDefinitions",
                columns: new[] { "Id", "Category", "EffectJson", "Key", "Name", "PricePremium", "PriceSoft" },
                values: new object[,]
                {
                    { 1, "Filter", "{\"restoreMaintenance\":100}", "filter_basic", "Filtro", null, 20m },
                    { 2, "AutoFilter", "{\"autoFilter\":true}", "auto_filter", "Filtro Automático", null, 500m },
                    { 3, "HabitatUpgrade", "{\"capacityDelta\":1}", "tank_upgrade", "Expansão do Tanque", null, 50m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
