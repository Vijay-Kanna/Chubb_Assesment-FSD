using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordClaimAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ApprovedAmountMinor",
                table: "Claims",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedCurrency",
                table: "Claims",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RequestedAmountMinor",
                table: "Claims",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedCurrency",
                table: "Claims",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_CreatedAt",
                table: "Claims",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Claims_CreatedAt",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ApprovedAmountMinor",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ApprovedCurrency",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "RequestedAmountMinor",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "RequestedCurrency",
                table: "Claims");
        }
    }
}
