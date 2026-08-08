using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparateTankProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemDefinitions",
                columns: new[] { "Id", "Category", "EffectJson", "Key", "Name", "PricePremium", "PriceSoft" },
                values: new object[,]
                {
                    { 6, "HabitatUpgrade", "{\"capacityDelta\":1}", "aquario_grande", "Aquário Grande", null, 4000m },
                    { 7, "HabitatUpgrade", "{\"capacityDelta\":1}", "aquario_master", "Aquário Master", null, 12000m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 7);
        }
    }
}
