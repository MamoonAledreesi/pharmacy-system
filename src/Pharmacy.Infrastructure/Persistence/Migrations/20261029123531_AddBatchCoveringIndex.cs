using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pharmacy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Batches_ProductId_ExpiryDate",
                schema: "inventory",
                table: "Batches");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_ExpiryDate",
                schema: "inventory",
                table: "Batches",
                columns: new[] { "ProductId", "ExpiryDate" })
                .Annotation("SqlServer:Include", new[] { "QuantityAvailable", "QuantityQuarantined", "UnitCost" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Batches_ProductId_ExpiryDate",
                schema: "inventory",
                table: "Batches");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_ExpiryDate",
                schema: "inventory",
                table: "Batches",
                columns: new[] { "ProductId", "ExpiryDate" });
        }
    }
}
