using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "expenses");

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameArabic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEnglish = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Payee = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReverseReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReverseOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Expenses_Reversal", "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "expenses",
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "expenses",
                table: "ExpenseCategories",
                columns: new[] { "Id", "IsActive", "NameArabic", "NameEnglish" },
                values: new object[,]
                {
                    { 1, true, "إيجار", "Rent" },
                    { 2, true, "كهرباء", "Electricity" },
                    { 3, true, "ماء", "Water" },
                    { 4, true, "اتصالات وإنترنت", "Phone and internet" },
                    { 5, true, "رواتب وأجور", "Salaries and wages" },
                    { 6, true, "نظافة", "Cleaning" },
                    { 7, true, "صيانة", "Maintenance" },
                    { 8, true, "مستلزمات مكتبية", "Office supplies" },
                    { 9, true, "نقل وتوصيل", "Transport and delivery" },
                    { 10, true, "رسوم حكومية", "Government fees" },
                    { 11, true, "أخرى", "Other" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 57, "permission", "Expenses.View", 2 },
                    { 58, "permission", "Expenses.View", 4 },
                    { 59, "permission", "Expenses.Create", 2 },
                    { 60, "permission", "Expenses.Create", 4 },
                    { 61, "permission", "Expenses.Reverse", 4 },
                    { 62, "permission", "Expenses.Manage", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_NameArabic",
                schema: "expenses",
                table: "ExpenseCategories",
                column: "NameArabic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_NameEnglish",
                schema: "expenses",
                table: "ExpenseCategories",
                column: "NameEnglish",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId_ExpenseDate",
                schema: "expenses",
                table: "Expenses",
                columns: new[] { "CategoryId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseDate",
                schema: "expenses",
                table: "Expenses",
                column: "ExpenseDate");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Number",
                schema: "expenses",
                table: "Expenses",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OperationId",
                schema: "expenses",
                table: "Expenses",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ReverseOperationId",
                schema: "expenses",
                table: "Expenses",
                column: "ReverseOperationId",
                unique: true,
                filter: "[ReverseOperationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "ExpenseCategories",
                schema: "expenses");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 62);
        }
    }
}
