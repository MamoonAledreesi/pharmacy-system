using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtReduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashRefund = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.Id);
                    table.CheckConstraint("CK_PurchaseReturns_Amounts", "[Total] > 0 AND [DebtReduction] >= 0 AND [CashRefund] >= 0 AND [DebtReduction] + [CashRefund] = [Total]");
                    table.CheckConstraint("CK_PurchaseReturns_DebtNeedsSupplier", "[DebtReduction] = 0 OR [SupplierId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_PurchaseInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "purchasing",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnLines",
                schema: "purchasing",
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
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    FromQuarantine = table.Column<bool>(type: "bit", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnLines", x => x.Id);
                    table.CheckConstraint("CK_PurchaseReturnLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
                    table.CheckConstraint("CK_PurchaseReturnLines_Value", "[Value] >= 0");
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "inventory",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalSchema: "catalog",
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseInvoiceLines_InvoiceLineId",
                        column: x => x.InvoiceLineId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseReturns_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[] { 56, "permission", "Purchasing.Return", 5 });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_BatchId",
                schema: "purchasing",
                table: "PurchaseReturnLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_InvoiceLineId",
                schema: "purchasing",
                table: "PurchaseReturnLines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProductId",
                schema: "purchasing",
                table: "PurchaseReturnLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProductUnitId",
                schema: "purchasing",
                table: "PurchaseReturnLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ReturnId",
                schema: "purchasing",
                table: "PurchaseReturnLines",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_CreatedAtUtc",
                schema: "purchasing",
                table: "PurchaseReturns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_InvoiceId",
                schema: "purchasing",
                table: "PurchaseReturns",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_Number",
                schema: "purchasing",
                table: "PurchaseReturns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_OperationId",
                schema: "purchasing",
                table: "PurchaseReturns",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_SupplierId",
                schema: "purchasing",
                table: "PurchaseReturns",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReturnLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseReturns",
                schema: "purchasing");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 56);
        }
    }
}
