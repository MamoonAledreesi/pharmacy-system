using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnWindowDays",
                schema: "config",
                table: "PharmacySettings",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.CreateTable(
                name: "SalesReturns",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtReduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturns", x => x.Id);
                    table.CheckConstraint("CK_SalesReturns_Amounts", "[Total] > 0 AND [DebtReduction] >= 0 AND [CashRefund] >= 0 AND [DebtReduction] + [CashRefund] = [Total]");
                    table.ForeignKey(
                        name: "FK_SalesReturns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "receivables",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturns_SalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sales",
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnId = table.Column<int>(type: "int", nullable: false),
                    InvoiceLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitId = table.Column<int>(type: "int", nullable: false),
                    UnitFactor = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnLines", x => x.Id);
                    table.CheckConstraint("CK_SalesReturnLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
                    table.CheckConstraint("CK_SalesReturnLines_Value", "[Value] >= 0");
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalSchema: "catalog",
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_SalesInvoiceLines_InvoiceLineId",
                        column: x => x.InvoiceLineId,
                        principalSchema: "sales",
                        principalTable: "SalesInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_SalesReturns_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "sales",
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnAllocations",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnLineId = table.Column<int>(type: "int", nullable: false),
                    InvoiceAllocationId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quarantined = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnAllocations", x => x.Id);
                    table.CheckConstraint("CK_SalesReturnAllocations_Amounts", "[BaseQuantity] > 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_SalesReturnAllocations_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "inventory",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnAllocations_SalesLineAllocations_InvoiceAllocationId",
                        column: x => x.InvoiceAllocationId,
                        principalSchema: "sales",
                        principalTable: "SalesLineAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnAllocations_SalesReturnLines_ReturnLineId",
                        column: x => x.ReturnLineId,
                        principalSchema: "sales",
                        principalTable: "SalesReturnLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "config",
                table: "PharmacySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ReturnWindowDays",
                value: 14);

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 53, "permission", "Sales.Return", 2 },
                    { 54, "permission", "Sales.Return", 3 },
                    { 55, "permission", "Sales.LateReturn", 2 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PharmacySettings_ReturnWindowDays",
                schema: "config",
                table: "PharmacySettings",
                sql: "[ReturnWindowDays] BETWEEN 0 AND 365");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnAllocations_BatchId",
                schema: "sales",
                table: "SalesReturnAllocations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnAllocations_InvoiceAllocationId",
                schema: "sales",
                table: "SalesReturnAllocations",
                column: "InvoiceAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnAllocations_ReturnLineId",
                schema: "sales",
                table: "SalesReturnAllocations",
                column: "ReturnLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_InvoiceLineId",
                schema: "sales",
                table: "SalesReturnLines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_ProductId",
                schema: "sales",
                table: "SalesReturnLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_ProductUnitId",
                schema: "sales",
                table: "SalesReturnLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_ReturnId",
                schema: "sales",
                table: "SalesReturnLines",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_CreatedAtUtc",
                schema: "sales",
                table: "SalesReturns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_CustomerId",
                schema: "sales",
                table: "SalesReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_InvoiceId",
                schema: "sales",
                table: "SalesReturns",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_Number",
                schema: "sales",
                table: "SalesReturns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_OperationId",
                schema: "sales",
                table: "SalesReturns",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesReturnAllocations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SalesReturnLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SalesReturns",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PharmacySettings_ReturnWindowDays",
                schema: "config",
                table: "PharmacySettings");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DropColumn(
                name: "ReturnWindowDays",
                schema: "config",
                table: "PharmacySettings");
        }
    }
}
