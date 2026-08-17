using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RaiseLegendaryEggBias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "EffectJson",
                value: "{\"eggBiasStrength\":0.75}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "EffectJson",
                value: "{\"eggBiasStrength\":0.55}");
        }
    }
}
