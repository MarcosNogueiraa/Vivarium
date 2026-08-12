using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterSensor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AutoCleanTriggerPercent",
                table: "Habitats",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "HasWaterSensor",
                table: "Habitats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "ItemDefinitions",
                columns: new[] { "Id", "Category", "EffectJson", "Key", "Name", "PricePremium", "PriceSoft" },
                values: new object[] { 8, "WaterSensor", "{}", "water_sensor", "Sensor de Qualidade da Água", null, 800m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DropColumn(
                name: "AutoCleanTriggerPercent",
                table: "Habitats");

            migrationBuilder.DropColumn(
                name: "HasWaterSensor",
                table: "Habitats");
        }
    }
}
