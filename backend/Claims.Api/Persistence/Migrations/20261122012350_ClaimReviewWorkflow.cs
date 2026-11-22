using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimMessages_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimMessages_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // SQLite can add a nullable reference inline without rebuilding the history table.
            migrationBuilder.Sql("""
                ALTER TABLE "ClaimHistory" ADD COLUMN "ClaimMessageId" INTEGER
                    CONSTRAINT "FK_ClaimHistory_ClaimMessages_ClaimMessageId"
                    REFERENCES "ClaimMessages" ("Id") ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimHistory_ClaimMessageId",
                table: "ClaimHistory",
                column: "ClaimMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimMessages_ClaimId_Id",
                table: "ClaimMessages",
                columns: new[] { "ClaimId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimMessages_SenderUserId",
                table: "ClaimMessages",
                column: "SenderUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClaimHistory_ClaimMessageId",
                table: "ClaimHistory");

            migrationBuilder.Sql("""ALTER TABLE "ClaimHistory" DROP COLUMN "ClaimMessageId";""");

            migrationBuilder.DropTable(
                name: "ClaimMessages");
        }
    }
}
