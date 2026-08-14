using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAndOriginalOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OriginalOwnerId ("primeiro dono", CLAUDE.md §8.24) precisa existir pros ~40 peixes
            // já existentes antes de virar NOT NULL — não há histórico real de quem coletou
            // primeiro, então o backfill é retroativo (decisão confirmada com o usuário): o dono
            // ATUAL vira o "primeiro dono" de todo peixe já existente. Nullable → UPDATE → NOT
            // NULL, tudo nesta mesma migration (mais simples de operar que 2 migrations em
            // sequência).
            migrationBuilder.AddColumn<long>(
                name: "OriginalOwnerId",
                table: "CreatureInstances",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "CreatureInstances" SET "OriginalOwnerId" = "OwnerId" WHERE "OriginalOwnerId" IS NULL""");

            migrationBuilder.AlterColumn<long>(
                name: "OriginalOwnerId",
                table: "CreatureInstances",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingInboxClaim",
                table: "CreatureInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedByAdminId = table.Column<long>(type: "bigint", nullable: false),
                    Audience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RewardCurrencyTypeId = table.Column<int>(type: "integer", nullable: true),
                    RewardCurrencyAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RewardItemDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    RewardItemQuantity = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxMessages_CurrencyTypes_RewardCurrencyTypeId",
                        column: x => x.RewardCurrencyTypeId,
                        principalTable: "CurrencyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboxMessages_ItemDefinitions_RewardItemDefinitionId",
                        column: x => x.RewardItemDefinitionId,
                        principalTable: "ItemDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboxMessages_Users_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InboxEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecipientId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    InboxMessageId = table.Column<long>(type: "bigint", nullable: true),
                    SenderUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatureInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxEntries_CreatureInstances_CreatureInstanceId",
                        column: x => x.CreatureInstanceId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboxEntries_InboxMessages_InboxMessageId",
                        column: x => x.InboxMessageId,
                        principalTable: "InboxMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InboxEntries_Users_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InboxEntries_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_OriginalOwnerId",
                table: "CreatureInstances",
                column: "OriginalOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_PendingInboxClaim",
                table: "CreatureInstances",
                column: "PendingInboxClaim");

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_CreatureInstanceId",
                table: "InboxEntries",
                column: "CreatureInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_InboxMessageId",
                table: "InboxEntries",
                column: "InboxMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_RecipientId_ClaimedAt",
                table: "InboxEntries",
                columns: new[] { "RecipientId", "ClaimedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_SenderUserId",
                table: "InboxEntries",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_CreatedByAdminId",
                table: "InboxMessages",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_RewardCurrencyTypeId",
                table: "InboxMessages",
                column: "RewardCurrencyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_RewardItemDefinitionId",
                table: "InboxMessages",
                column: "RewardItemDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreatureInstances_Users_OriginalOwnerId",
                table: "CreatureInstances",
                column: "OriginalOwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreatureInstances_Users_OriginalOwnerId",
                table: "CreatureInstances");

            migrationBuilder.DropTable(
                name: "InboxEntries");

            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_CreatureInstances_OriginalOwnerId",
                table: "CreatureInstances");

            migrationBuilder.DropIndex(
                name: "IX_CreatureInstances_PendingInboxClaim",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "OriginalOwnerId",
                table: "CreatureInstances");

            migrationBuilder.DropColumn(
                name: "PendingInboxClaim",
                table: "CreatureInstances");
        }
    }
}
