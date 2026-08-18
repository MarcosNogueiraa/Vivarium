using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AvatarCreatureInstanceId",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Xp",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "CoinsPerHourSnapshot",
                table: "Habitats",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AvatarCreatureInstanceId",
                table: "Users",
                column: "AvatarCreatureInstanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_CreatureInstances_AvatarCreatureInstanceId",
                table: "Users",
                column: "AvatarCreatureInstanceId",
                principalTable: "CreatureInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_CreatureInstances_AvatarCreatureInstanceId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_AvatarCreatureInstanceId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarCreatureInstanceId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CoinsPerHourSnapshot",
                table: "Habitats");
        }
    }
}
