using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTraitsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BreedingSourceJson",
                table: "CreatureInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraitsJson",
                table: "CreatureInstances",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreedingSourceJson",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "TraitsJson",
                table: "CreatureInstances");
        }
    }
}
