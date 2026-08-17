using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEggItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemDefinitions",
                columns: new[] { "Id", "Category", "EffectJson", "Key", "Name", "PricePremium", "PriceSoft" },
                values: new object[,]
                {
                    { 9, "Egg", "{\"eggBiasStrength\":0.15}", "egg_common", "Ovo Comum", 8m, 0m },
                    { 10, "Egg", "{\"eggBiasStrength\":0.35}", "egg_rare", "Ovo Raro", 30m, 0m },
                    { 11, "Egg", "{\"eggBiasStrength\":0.55}", "egg_legendary", "Ovo Lendário", 90m, 0m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 11);
        }
    }
}
