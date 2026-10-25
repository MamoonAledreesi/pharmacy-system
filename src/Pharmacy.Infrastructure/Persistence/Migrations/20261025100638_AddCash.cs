using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cash");

            migrationBuilder.CreateTable(
                name: "CashShifts",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpenedByUserId = table.Column<int>(type: "int", nullable: true),
                    OpenedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PreviousCountedBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OpenOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CountedBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    WasForceClosed = table.Column<bool>(type: "bit", nullable: false),
                    CloseOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                    table.CheckConstraint("CK_CashShifts_Balances", "[OpeningBalance] >= 0 AND [CurrentBalance] >= 0 AND [PreviousCountedBalance] >= 0");
                    table.CheckConstraint("CK_CashShifts_OpenOrClosed", "([IsOpen] = 1 AND [ClosedAtUtc] IS NULL AND [CountedBalance] IS NULL AND [Difference] IS NULL) OR ([IsOpen] = 0 AND [ClosedAtUtc] IS NOT NULL AND [CountedBalance] IS NOT NULL AND [Difference] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.CheckConstraint("CK_CashMovements_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_CashMovements_BalanceAfter", "[BalanceAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_CashMovements_CashShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalSchema: "cash",
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 18, "permission", "Cash.View", 2 },
                    { 19, "permission", "Cash.View", 3 },
                    { 20, "permission", "Cash.View", 4 },
                    { 21, "permission", "Cash.OpenShift", 2 },
                    { 22, "permission", "Cash.OpenShift", 3 },
                    { 23, "permission", "Cash.OpenShift", 4 },
                    { 24, "permission", "Cash.ManualMovement", 2 },
                    { 25, "permission", "Cash.ManualMovement", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_OccurredAtUtc",
                schema: "cash",
                table: "CashMovements",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_OperationId",
                schema: "cash",
                table: "CashMovements",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ReferenceType_ReferenceId",
                schema: "cash",
                table: "CashMovements",
                columns: new[] { "ReferenceType", "ReferenceId" },
                filter: "[ReferenceType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ShiftId_Id",
                schema: "cash",
                table: "CashMovements",
                columns: new[] { "ShiftId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_CloseOperationId",
                schema: "cash",
                table: "CashShifts",
                column: "CloseOperationId",
                unique: true,
                filter: "[CloseOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_OpenedAtUtc",
                schema: "cash",
                table: "CashShifts",
                column: "OpenedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_OpenOperationId",
                schema: "cash",
                table: "CashShifts",
                column: "OpenOperationId",
                unique: true,
                filter: "[OpenOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CashShifts_OneOpen",
                schema: "cash",
                table: "CashShifts",
                column: "IsOpen",
                unique: true,
                filter: "[IsOpen] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovements",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "CashShifts",
                schema: "cash");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 25);
        }
    }
}
