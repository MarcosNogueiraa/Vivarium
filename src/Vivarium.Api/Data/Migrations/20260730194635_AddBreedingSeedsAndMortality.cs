using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingSeedsAndMortality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BreedCount",
                table: "CreatureInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiedAt",
                table: "CreatureInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDead",
                table: "CreatureInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ParentASeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParentBSeed",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreedCount",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "DiedAt",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "IsDead",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "ParentASeed",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "ParentBSeed",
                table: "CreatureInstances");
        }
    }
}
