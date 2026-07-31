using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGrandparentSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ParentAGrandparentASeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParentAGrandparentBSeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParentBGrandparentASeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParentBGrandparentBSeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentAGrandparentASeed",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "ParentAGrandparentBSeed",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "ParentBGrandparentASeed",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "ParentBGrandparentBSeed",
                table: "CreatureInstances");
        }
    }
}
