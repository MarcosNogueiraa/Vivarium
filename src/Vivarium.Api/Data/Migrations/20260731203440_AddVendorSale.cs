using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SoldAt",
                table: "CreatureInstances",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SoldAt",
                table: "CreatureInstances");
        }
    }
}
