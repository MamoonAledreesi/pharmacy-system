using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountPercent",
                schema: "config",
                table: "PharmacySettings",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OnCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BelowCostApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    BelowCostReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DiscountApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoices_Amounts", "[Subtotal] > 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] < [Subtotal] AND [Total] = [Subtotal] - [DiscountAmount] AND [PaidCash] >= 0 AND [OnCredit] >= 0 AND [PaidCash] + [OnCredit] = [Total]");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "receivables",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitId = table.Column<int>(type: "int", nullable: false),
                    UnitFactor = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountShare = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoiceLines_Amounts", "[UnitPrice] > 0 AND [LineSubtotal] > 0 AND [DiscountShare] >= 0 AND [NetTotal] = [LineSubtotal] - [DiscountShare] AND [CostTotal] >= 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_ProductUnits_ProductUnitId",
                        column: x => x.ProductUnitId,
                        principalSchema: "catalog",
                        principalTable: "ProductUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sales",
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesLineAllocations",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsNearExpiry = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesLineAllocations", x => x.Id);
                    table.CheckConstraint("CK_SalesLineAllocations_Amounts", "[BaseQuantity] > 0 AND [UnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_SalesLineAllocations_Batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "inventory",
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesLineAllocations_SalesInvoiceLines_LineId",
                        column: x => x.LineId,
                        principalSchema: "sales",
                        principalTable: "SalesInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "config",
                table: "PharmacySettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "MaxDiscountPercent",
                value: 10m);

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 44, "permission", "Sales.View", 2 },
                    { 45, "permission", "Sales.View", 3 },
                    { 46, "permission", "Sales.View", 4 },
                    { 47, "permission", "Sales.Create", 2 },
                    { 48, "permission", "Sales.Create", 3 },
                    { 49, "permission", "Sales.ViewCost", 2 },
                    { 50, "permission", "Sales.ViewCost", 4 },
                    { 51, "permission", "Sales.GrantHighDiscount", 2 },
                    { 52, "permission", "Sales.SellBelowCost", 2 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PharmacySettings_MaxDiscountPercent",
                schema: "config",
                table: "PharmacySettings",
                sql: "[MaxDiscountPercent] BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_InvoiceId",
                schema: "sales",
                table: "SalesInvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProductId",
                schema: "sales",
                table: "SalesInvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_ProductUnitId",
                schema: "sales",
                table: "SalesInvoiceLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CreatedAtUtc",
                schema: "sales",
                table: "SalesInvoices",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CustomerId_CreatedAtUtc",
                schema: "sales",
                table: "SalesInvoices",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Number",
                schema: "sales",
                table: "SalesInvoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_OperationId",
                schema: "sales",
                table: "SalesInvoices",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesLineAllocations_BatchId",
                schema: "sales",
                table: "SalesLineAllocations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesLineAllocations_LineId",
                schema: "sales",
                table: "SalesLineAllocations",
                column: "LineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesLineAllocations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SalesInvoices",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PharmacySettings_MaxDiscountPercent",
                schema: "config",
                table: "PharmacySettings");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DropColumn(
                name: "MaxDiscountPercent",
                schema: "config",
                table: "PharmacySettings");
        }
    }
}
