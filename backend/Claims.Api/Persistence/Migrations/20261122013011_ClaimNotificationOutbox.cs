using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Claims.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimNotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimNotificationOutbox_EventId",
                table: "ClaimNotificationOutbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimNotificationOutbox_PublishedAt_Id",
                table: "ClaimNotificationOutbox",
                columns: new[] { "PublishedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimNotificationOutbox");
        }
    }
}
