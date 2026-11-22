using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimExposureEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstimatedExposureCurrency",
                table: "Claims",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedExposureMinor",
                table: "Claims",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedExposureCurrency",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "EstimatedExposureMinor",
                table: "Claims");
        }
    }
}
