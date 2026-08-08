using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFilterTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "EffectJson",
                value: "{\"filterCapacity\":5}");

            migrationBuilder.InsertData(
                table: "ItemDefinitions",
                columns: new[] { "Id", "Category", "EffectJson", "Key", "Name", "PricePremium", "PriceSoft" },
                values: new object[,]
                {
                    { 4, "AutoFilter", "{\"filterCapacity\":10}", "auto_filter_2", "Filtro Automático II", null, 1200m },
                    { 5, "AutoFilter", "{\"filterCapacity\":18}", "auto_filter_3", "Filtro Automático III", null, 2500m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.UpdateData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "EffectJson",
                value: "{\"autoFilter\":true}");
        }
    }
}
