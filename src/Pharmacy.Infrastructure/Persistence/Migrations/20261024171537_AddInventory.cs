using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.AddColumn<int>(
                name: "NearExpiryDays",
                schema: "config",
                table: "PharmacySettings",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateTable(
                name: "Batches",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityAvailable = table.Column<int>(type: "int", nullable: false),
                    QuantityQuarantined = table.Column<int>(type: "int", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.CheckConstraint("CK_Batches_QuantityAvailable", "[QuantityAvailable] >= 0");
                    table.CheckConstraint("CK_Batches_QuantityQuarantined", "[QuantityQuarantined] >= 0");
                    table.CheckConstraint("CK_Batches_UnitCost", "[UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_Batches_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AvailableDelta = table.Column<int>(type: "int", nullable: false),
                    QuarantineDelta = table.Column<int>(type: "int", nullable: false),
                    AvailableAfter = table.Column<int>(type: "int", nullable: false),
                    QuarantineAfter = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Balances", "[AvailableAfter] >= 0 AND [QuarantineAfter] >= 0");
                    table.CheckConstraint("CK_StockMovements_NotEmpty", "[AvailableDelta] <> 0 OR [QuarantineDelta] <> 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "inventory",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "config",
                table: "PharmacySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "NearExpiryDays",
                value: 90);

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 7, "permission", "Inventory.View", 2 },
                    { 8, "permission", "Inventory.View", 3 },
                    { 9, "permission", "Inventory.View", 4 },
                    { 10, "permission", "Inventory.View", 5 },
                    { 11, "permission", "Inventory.Adjust", 2 },
                    { 12, "permission", "Inventory.Adjust", 5 },
                    { 13, "permission", "Inventory.WriteOff", 2 },
                    { 14, "permission", "Inventory.WriteOff", 5 },
                    { 15, "permission", "Inventory.OpeningStock", 5 },
                    { 16, "permission", "Inventory.ViewCost", 2 },
                    { 17, "permission", "Inventory.ViewCost", 4 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PharmacySettings_NearExpiryDays",
                schema: "config",
                table: "PharmacySettings",
                sql: "[NearExpiryDays] BETWEEN 1 AND 3650");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ExpiryDate",
                schema: "inventory",
                table: "Batches",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_BatchNumber_ExpiryDate_UnitCost",
                schema: "inventory",
                table: "Batches",
                columns: new[] { "ProductId", "BatchNumber", "ExpiryDate", "UnitCost" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_ExpiryDate",
                schema: "inventory",
                table: "Batches",
                columns: new[] { "ProductId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId_Id",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "BatchId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OperationId_BatchId_Type",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "OperationId", "BatchId", "Type" },
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_OccurredAtUtc",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "ProductId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReferenceType_ReferenceId",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "ReferenceType", "ReferenceId" },
                filter: "[ReferenceType] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Batches",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PharmacySettings_NearExpiryDays",
                schema: "config",
                table: "PharmacySettings");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DropColumn(
                name: "NearExpiryDays",
                schema: "config",
                table: "PharmacySettings");
        }
    }
}
