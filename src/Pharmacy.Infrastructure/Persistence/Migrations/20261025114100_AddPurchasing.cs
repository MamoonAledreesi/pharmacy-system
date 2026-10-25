using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.EnsureSchema(
                name: "payables");

            migrationBuilder.CreateTable(
                name: "DocumentSequences",
                schema: "config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(181)", maxLength: 181, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.CheckConstraint("CK_Suppliers_Balance", "[CurrentBalance] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OnCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.CheckConstraint("CK_PurchaseInvoices_Amounts", "[Total] >= 0 AND [PaidCash] >= 0 AND [OnCredit] >= 0 AND [PaidCash] + [OnCredit] = [Total]");
                    table.CheckConstraint("CK_PurchaseInvoices_CreditNeedsSupplier", "[OnCredit] = 0 OR [SupplierId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "purchasing",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierAccountEntries",
                schema: "payables",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierAccountEntries", x => x.Id);
                    table.CheckConstraint("CK_SupplierAccountEntries_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_SupplierAccountEntries_BalanceAfter", "[BalanceAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_SupplierAccountEntries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "purchasing",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                schema: "payables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReverseReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReverseOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.CheckConstraint("CK_SupplierPayments_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_SupplierPayments_Reversal", "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "purchasing",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLines",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitFactor = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<int>(type: "int", nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_PurchaseInvoiceLines_Costs", "[CostPerUnit] >= 0 AND [BaseUnitCost] >= 0 AND [LineTotal] >= 0");
                    table.CheckConstraint("CK_PurchaseInvoiceLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "inventory",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalSchema: "catalog",
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_PurchaseInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 26, "permission", "Suppliers.View", 2 },
                    { 27, "permission", "Suppliers.View", 4 },
                    { 28, "permission", "Suppliers.View", 5 },
                    { 29, "permission", "Suppliers.Manage", 4 },
                    { 30, "permission", "Suppliers.Manage", 5 },
                    { 31, "permission", "Purchasing.View", 2 },
                    { 32, "permission", "Purchasing.View", 4 },
                    { 33, "permission", "Purchasing.View", 5 },
                    { 34, "permission", "Purchasing.Create", 5 },
                    { 35, "permission", "Payables.Pay", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSequences_Prefix_Year",
                schema: "config",
                table: "DocumentSequences",
                columns: new[] { "Prefix", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_BatchId",
                schema: "purchasing",
                table: "PurchaseInvoiceLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_InvoiceId",
                schema: "purchasing",
                table: "PurchaseInvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_ProductId",
                schema: "purchasing",
                table: "PurchaseInvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_ProductUnitId",
                schema: "purchasing",
                table: "PurchaseInvoiceLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_InvoiceDate",
                schema: "purchasing",
                table: "PurchaseInvoices",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_Number",
                schema: "purchasing",
                table: "PurchaseInvoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_OperationId",
                schema: "purchasing",
                table: "PurchaseInvoices",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId_InvoiceDate",
                schema: "purchasing",
                table: "PurchaseInvoices",
                columns: new[] { "SupplierId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccountEntries_OperationId",
                schema: "payables",
                table: "SupplierAccountEntries",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccountEntries_ReferenceType_ReferenceId",
                schema: "payables",
                table: "SupplierAccountEntries",
                columns: new[] { "ReferenceType", "ReferenceId" },
                filter: "[ReferenceType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAccountEntries_SupplierId_Id",
                schema: "payables",
                table: "SupplierAccountEntries",
                columns: new[] { "SupplierId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_OperationId",
                schema: "payables",
                table: "SupplierPayments",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_ReverseOperationId",
                schema: "payables",
                table: "SupplierPayments",
                column: "ReverseOperationId",
                unique: true,
                filter: "[ReverseOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId_Id",
                schema: "payables",
                table: "SupplierPayments",
                columns: new[] { "SupplierId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                schema: "purchasing",
                table: "Suppliers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSequences",
                schema: "config");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "SupplierAccountEntries",
                schema: "payables");

            migrationBuilder.DropTable(
                name: "SupplierPayments",
                schema: "payables");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "Suppliers",
                schema: "purchasing");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 35);
        }
    }
}
