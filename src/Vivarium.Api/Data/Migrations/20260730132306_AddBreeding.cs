using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBreeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BreedingSlots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    HabitatId = table.Column<long>(type: "bigint", nullable: false),
                    ParentAId = table.Column<long>(type: "bigint", nullable: false),
                    ParentBId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChildCreatureId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BreedingSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BreedingSlots_CreatureInstances_ChildCreatureId",
                        column: x => x.ChildCreatureId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BreedingSlots_CreatureInstances_ParentAId",
                        column: x => x.ParentAId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BreedingSlots_CreatureInstances_ParentBId",
                        column: x => x.ParentBId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BreedingSlots_Habitats_HabitatId",
                        column: x => x.HabitatId,
                        principalTable: "Habitats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BreedingSlots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "HabitatTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[] { 2, "Breeding", "Ninho" });

            migrationBuilder.CreateIndex(
                name: "IX_BreedingSlots_ChildCreatureId",
                table: "BreedingSlots",
                column: "ChildCreatureId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingSlots_HabitatId",
                table: "BreedingSlots",
                column: "HabitatId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingSlots_ParentAId",
                table: "BreedingSlots",
                column: "ParentAId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingSlots_ParentBId",
                table: "BreedingSlots",
                column: "ParentBId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingSlots_UserId_Status",
                table: "BreedingSlots",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BreedingSlots");

            migrationBuilder.DeleteData(
                table: "HabitatTypes",
                keyColumn: "Id",
                keyValue: 2);
        }
    }
}
