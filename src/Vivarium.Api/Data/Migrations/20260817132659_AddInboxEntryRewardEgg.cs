using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxEntryRewardEgg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RewardItemDefinitionId",
                table: "InboxEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_RewardItemDefinitionId",
                table: "InboxEntries",
                column: "RewardItemDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_InboxEntries_ItemDefinitions_RewardItemDefinitionId",
                table: "InboxEntries",
                column: "RewardItemDefinitionId",
                principalTable: "ItemDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InboxEntries_ItemDefinitions_RewardItemDefinitionId",
                table: "InboxEntries");

            migrationBuilder.DropIndex(
                name: "IX_InboxEntries_RewardItemDefinitionId",
                table: "InboxEntries");

            migrationBuilder.DropColumn(
                name: "RewardItemDefinitionId",
                table: "InboxEntries");
        }
    }
}
