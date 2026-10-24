using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "config");

            migrationBuilder.CreateTable(
                name: "PharmacySettings",
                schema: "config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NameArabic = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEnglish = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ThemeMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrimaryColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PharmacySettings", x => x.Id);
                    table.CheckConstraint("CK_PharmacySettings_SingleRow", "[Id] = 1");
                });

            migrationBuilder.InsertData(
                schema: "config",
                table: "PharmacySettings",
                columns: new[] { "Id", "Address", "NameArabic", "NameEnglish", "Phone", "PrimaryColor", "ThemeMode" },
                values: new object[] { 1, null, "الصيدلية", "Pharmacy", null, "MedicalGreen", "Light" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PharmacySettings",
                schema: "config");
        }
    }
}
