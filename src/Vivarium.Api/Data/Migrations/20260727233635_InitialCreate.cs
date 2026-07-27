using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurrencyTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HabitatTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitatTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EffectJson = table.Column<string>(type: "text", nullable: false),
                    PriceSoft = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PricePremium = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FromUserId = table.Column<long>(type: "bigint", nullable: true),
                    ToUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatureInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyTypeId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Species",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HabitatTypeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaseSpriteKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Species", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Species_HabitatTypes_HabitatTypeId",
                        column: x => x.HabitatTypeId,
                        principalTable: "HabitatTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Habitats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    HabitatTypeId = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    MaintenanceLevel = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    QueueCap = table.Column<int>(type: "integer", nullable: false),
                    GenerationIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    OnlineGenerationRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    OfflineGenerationRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    LastTickAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Habitats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Habitats_HabitatTypes_HabitatTypeId",
                        column: x => x.HabitatTypeId,
                        principalTable: "HabitatTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Habitats_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInventories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ItemDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInventories_ItemDefinitions_ItemDefinitionId",
                        column: x => x.ItemDefinitionId,
                        principalTable: "ItemDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserInventories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VipSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VipSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletBalances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyTypeId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletBalances_CurrencyTypes_CurrencyTypeId",
                        column: x => x.CurrencyTypeId,
                        principalTable: "CurrencyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletBalances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TraitWeightConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeciesId = table.Column<int>(type: "integer", nullable: true),
                    PartType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TraitCategory = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraitWeightConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TraitWeightConfigs_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CreatureInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpeciesId = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    HabitatId = table.Column<long>(type: "bigint", nullable: true),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    TraitConfigVersion = table.Column<int>(type: "integer", nullable: false),
                    RarityScore = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ParentAId = table.Column<long>(type: "bigint", nullable: true),
                    ParentBId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreatureInstances_CreatureInstances_ParentAId",
                        column: x => x.ParentAId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatureInstances_CreatureInstances_ParentBId",
                        column: x => x.ParentBId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatureInstances_Habitats_HabitatId",
                        column: x => x.HabitatId,
                        principalTable: "Habitats",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreatureInstances_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreatureInstances_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenerationQueueItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HabitatId = table.Column<long>(type: "bigint", nullable: false),
                    SpeciesId = table.Column<int>(type: "integer", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenerationQueueItems_Habitats_HabitatId",
                        column: x => x.HabitatId,
                        principalTable: "Habitats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GenerationQueueItems_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketListings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatureInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    SellerId = table.Column<long>(type: "bigint", nullable: false),
                    BuyerId = table.Column<long>(type: "bigint", nullable: true),
                    PriceSoft = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketListings_CreatureInstances_CreatureInstanceId",
                        column: x => x.CreatureInstanceId,
                        principalTable: "CreatureInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarketListings_Users_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MarketListings_Users_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CurrencyTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "SOFT", "Moeda Soft" },
                    { 2, "PREMIUM", "Moeda Premium" }
                });

            migrationBuilder.InsertData(
                table: "HabitatTypes",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[] { 1, "Aquarium", "Aquário" });

            migrationBuilder.InsertData(
                table: "Species",
                columns: new[] { "Id", "BaseSpriteKey", "HabitatTypeId", "Name" },
                values: new object[] { 1, "fish_base_gray", 1, "Tetra Base" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_HabitatId",
                table: "CreatureInstances",
                column: "HabitatId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_OwnerId",
                table: "CreatureInstances",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_ParentAId",
                table: "CreatureInstances",
                column: "ParentAId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_ParentBId",
                table: "CreatureInstances",
                column: "ParentBId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatureInstances_SpeciesId",
                table: "CreatureInstances",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyTypes_Code",
                table: "CurrencyTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationQueueItems_HabitatId_Status",
                table: "GenerationQueueItems",
                columns: new[] { "HabitatId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationQueueItems_SpeciesId",
                table: "GenerationQueueItems",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_Habitats_HabitatTypeId",
                table: "Habitats",
                column: "HabitatTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Habitats_UserId",
                table: "Habitats",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HabitatTypes_Code",
                table: "HabitatTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemDefinitions_Key",
                table: "ItemDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_BuyerId",
                table: "MarketListings",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_CreatureInstanceId",
                table: "MarketListings",
                column: "CreatureInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_SellerId",
                table: "MarketListings",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_Status",
                table: "MarketListings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Species_HabitatTypeId",
                table: "Species",
                column: "HabitatTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TraitWeightConfigs_SpeciesId_PartType_TraitCategory_Version",
                table: "TraitWeightConfigs",
                columns: new[] { "SpeciesId", "PartType", "TraitCategory", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLogs_CreatedAt",
                table: "TransactionLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLogs_FromUserId",
                table: "TransactionLogs",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLogs_ToUserId",
                table: "TransactionLogs",
                column: "ToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInventories_ItemDefinitionId",
                table: "UserInventories",
                column: "ItemDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInventories_UserId_ItemDefinitionId",
                table: "UserInventories",
                columns: new[] { "UserId", "ItemDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VipSubscriptions_UserId_Status",
                table: "VipSubscriptions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletBalances_CurrencyTypeId",
                table: "WalletBalances",
                column: "CurrencyTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletBalances_UserId_CurrencyTypeId",
                table: "WalletBalances",
                columns: new[] { "UserId", "CurrencyTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenerationQueueItems");

            migrationBuilder.DropTable(
                name: "MarketListings");

            migrationBuilder.DropTable(
                name: "TraitWeightConfigs");

            migrationBuilder.DropTable(
                name: "TransactionLogs");

            migrationBuilder.DropTable(
                name: "UserInventories");

            migrationBuilder.DropTable(
                name: "VipSubscriptions");

            migrationBuilder.DropTable(
                name: "WalletBalances");

            migrationBuilder.DropTable(
                name: "CreatureInstances");

            migrationBuilder.DropTable(
                name: "ItemDefinitions");

            migrationBuilder.DropTable(
                name: "CurrencyTypes");

            migrationBuilder.DropTable(
                name: "Habitats");

            migrationBuilder.DropTable(
                name: "Species");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "HabitatTypes");
        }
    }
}
