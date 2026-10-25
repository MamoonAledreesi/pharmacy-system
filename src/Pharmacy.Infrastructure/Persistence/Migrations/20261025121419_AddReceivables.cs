using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "receivables");

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "receivables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefaultCash = table.Column<bool>(type: "bit", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(181)", maxLength: 181, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.CheckConstraint("CK_Customers_Balance", "[CurrentBalance] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountEntries",
                schema: "receivables",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_CustomerAccountEntries", x => x.Id);
                    table.CheckConstraint("CK_CustomerAccountEntries_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_CustomerAccountEntries_BalanceAfter", "[BalanceAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_CustomerAccountEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "receivables",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DebtCollections",
                schema: "receivables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_DebtCollections", x => x.Id);
                    table.CheckConstraint("CK_DebtCollections_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_DebtCollections_Reversal", "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DebtCollections_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "receivables",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "receivables",
                table: "Customers",
                columns: new[] { "Id", "Address", "CurrentBalance", "IsActive", "IsDefaultCash", "Name", "Notes", "Phone", "SearchText" },
                values: new object[] { 1, null, 0m, true, true, "عميل نقدي / Cash customer", null, null, "عميل نقدي / cash customer" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 36, "permission", "Customers.View", 2 },
                    { 37, "permission", "Customers.View", 3 },
                    { 38, "permission", "Customers.View", 4 },
                    { 39, "permission", "Customers.Manage", 2 },
                    { 40, "permission", "Customers.Manage", 4 },
                    { 41, "permission", "Receivables.Collect", 2 },
                    { 42, "permission", "Receivables.Collect", 3 },
                    { 43, "permission", "Receivables.Collect", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountEntries_CustomerId_Id",
                schema: "receivables",
                table: "CustomerAccountEntries",
                columns: new[] { "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountEntries_OperationId",
                schema: "receivables",
                table: "CustomerAccountEntries",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountEntries_ReferenceType_ReferenceId",
                schema: "receivables",
                table: "CustomerAccountEntries",
                columns: new[] { "ReferenceType", "ReferenceId" },
                filter: "[ReferenceType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SearchText",
                schema: "receivables",
                table: "Customers",
                column: "SearchText");

            migrationBuilder.CreateIndex(
                name: "UX_Customers_OneDefaultCash",
                schema: "receivables",
                table: "Customers",
                column: "IsDefaultCash",
                unique: true,
                filter: "[IsDefaultCash] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DebtCollections_CustomerId_Id",
                schema: "receivables",
                table: "DebtCollections",
                columns: new[] { "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtCollections_Number",
                schema: "receivables",
                table: "DebtCollections",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebtCollections_OperationId",
                schema: "receivables",
                table: "DebtCollections",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebtCollections_ReverseOperationId",
                schema: "receivables",
                table: "DebtCollections",
                column: "ReverseOperationId",
                unique: true,
                filter: "[ReverseOperationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAccountEntries",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "DebtCollections",
                schema: "receivables");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "receivables");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 43);
        }
    }
}
