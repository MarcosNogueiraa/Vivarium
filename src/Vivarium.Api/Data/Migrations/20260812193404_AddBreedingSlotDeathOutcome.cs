using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingSlotDeathOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ParentADied",
                table: "BreedingSlots",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ParentBDied",
                table: "BreedingSlots",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentADied",
                table: "BreedingSlots");

            migrationBuilder.DropColumn(
                name: "ParentBDied",
                table: "BreedingSlots");
        }
    }
}
