using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Categories",
                schema: "catalog",
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
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DosageForms",
                schema: "catalog",
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
                    table.PrimaryKey("PK_DosageForms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitsOfMeasure",
                schema: "catalog",
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
                    table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeNameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TradeNameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ScientificName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Strength = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DosageFormId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TracksExpiry = table.Column<bool>(type: "bit", nullable: false),
                    MinimumStock = table.Column<int>(type: "int", nullable: true),
                    NearExpiryDays = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SearchText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_MinimumStock", "[MinimumStock] IS NULL OR [MinimumStock] >= 0");
                    table.CheckConstraint("CK_Products_NearExpiryDays", "[NearExpiryDays] IS NULL OR [NearExpiryDays] > 0");
                    table.CheckConstraint("CK_Products_TradeName", "[TradeNameArabic] IS NOT NULL OR [TradeNameEnglish] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "catalog",
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_DosageForms_DosageFormId",
                        column: x => x.DosageFormId,
                        principalSchema: "catalog",
                        principalTable: "DosageForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    UnitOfMeasureId = table.Column<int>(type: "int", nullable: false),
                    IsBase = table.Column<bool>(type: "bit", nullable: false),
                    Factor = table.Column<int>(type: "int", nullable: false),
                    DefaultSellingPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.Id);
                    table.CheckConstraint("CK_ProductUnits_Factor", "[Factor] >= 1 AND ([IsBase] = 0 OR [Factor] = 1)");
                    table.CheckConstraint("CK_ProductUnits_Price", "[DefaultSellingPrice] IS NULL OR [DefaultSellingPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductUnits_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalSchema: "catalog",
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Categories",
                columns: new[] { "Id", "IsActive", "NameArabic", "NameEnglish" },
                values: new object[] { 1, true, "أدوية عامة", "General medicines" });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "DosageForms",
                columns: new[] { "Id", "IsActive", "NameArabic", "NameEnglish" },
                values: new object[,]
                {
                    { 1, true, "أقراص", "Tablets" },
                    { 2, true, "كبسولات", "Capsules" },
                    { 3, true, "شراب", "Syrup" },
                    { 4, true, "حقن", "Injection" },
                    { 5, true, "كريم", "Cream" },
                    { 6, true, "مرهم", "Ointment" },
                    { 7, true, "قطرة", "Drops" },
                    { 8, true, "بخاخ", "Spray" },
                    { 9, true, "تحاميل", "Suppositories" },
                    { 10, true, "مسحوق", "Powder" },
                    { 11, true, "أخرى", "Other" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "RoleId" },
                values: new object[,]
                {
                    { 1, "permission", "Catalog.View", 2 },
                    { 2, "permission", "Catalog.Manage", 2 },
                    { 3, "permission", "Catalog.View", 3 },
                    { 4, "permission", "Catalog.View", 4 },
                    { 5, "permission", "Catalog.View", 5 },
                    { 6, "permission", "Catalog.Manage", 5 }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "UnitsOfMeasure",
                columns: new[] { "Id", "IsActive", "NameArabic", "NameEnglish" },
                values: new object[,]
                {
                    { 1, true, "حبة", "Tablet" },
                    { 2, true, "كبسولة", "Capsule" },
                    { 3, true, "شريط", "Strip" },
                    { 4, true, "علبة", "Box" },
                    { 5, true, "باكت", "Pack" },
                    { 6, true, "زجاجة", "Bottle" },
                    { 7, true, "أمبولة", "Ampoule" },
                    { 8, true, "أنبوب", "Tube" },
                    { 9, true, "كيس", "Sachet" },
                    { 10, true, "قطعة", "Piece" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NameArabic",
                schema: "catalog",
                table: "Categories",
                column: "NameArabic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NameEnglish",
                schema: "catalog",
                table: "Categories",
                column: "NameEnglish",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DosageForms_NameArabic",
                schema: "catalog",
                table: "DosageForms",
                column: "NameArabic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DosageForms_NameEnglish",
                schema: "catalog",
                table: "DosageForms",
                column: "NameEnglish",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                schema: "catalog",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_DosageFormId",
                schema: "catalog",
                table: "Products",
                column: "DosageFormId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_IsActive_TradeNameEnglish",
                schema: "catalog",
                table: "Products",
                columns: new[] { "IsActive", "TradeNameEnglish" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_Barcode",
                schema: "catalog",
                table: "ProductUnits",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_ProductId",
                schema: "catalog",
                table: "ProductUnits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_UnitOfMeasureId",
                schema: "catalog",
                table: "ProductUnits",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_NameArabic",
                schema: "catalog",
                table: "UnitsOfMeasure",
                column: "NameArabic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_NameEnglish",
                schema: "catalog",
                table: "UnitsOfMeasure",
                column: "NameEnglish",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductUnits",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "UnitsOfMeasure",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "DosageForms",
                schema: "catalog");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RoleClaims",
                keyColumn: "Id",
                keyValue: 6);
        }
    }
}
