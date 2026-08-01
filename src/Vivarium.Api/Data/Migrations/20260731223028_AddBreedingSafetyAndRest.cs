using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingSafetyAndRest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastBredAt",
                table: "CreatureInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InsuranceUsed",
                table: "BreedingSlots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ParentADeathChance",
                table: "BreedingSlots",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ParentBDeathChance",
                table: "BreedingSlots",
                type: "numeric(6,4)",
                precision: 6,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBredAt",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "InsuranceUsed",
                table: "BreedingSlots");

            migrationBuilder.DropColumn(
                name: "ParentADeathChance",
                table: "BreedingSlots");

            migrationBuilder.DropColumn(
                name: "ParentBDeathChance",
                table: "BreedingSlots");
        }
    }
}
