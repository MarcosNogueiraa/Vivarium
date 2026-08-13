using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoToggleAndIsNew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue TRUE (não o default gerado) — preserva o comportamento de sempre
            // (todo VIP online já coletava/limpava sozinho) pros habitats já existentes.
            migrationBuilder.AddColumn<bool>(
                name: "AutoCleanEnabled",
                table: "Habitats",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoCollectEnabled",
                table: "Habitats",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNew",
                table: "CreatureInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCleanEnabled",
                table: "Habitats");

            migrationBuilder.DropColumn(
                name: "AutoCollectEnabled",
                table: "Habitats");

            migrationBuilder.DropColumn(
                name: "IsNew",
                table: "CreatureInstances");
        }
    }
}
