using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddYtDlpMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "Media",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "UploadDate",
                table: "Media",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoId",
                table: "Media",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YtDlpMediaKind",
                table: "Media",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "YtDlpImportFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    PlaylistUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YtDlpImportFailures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Media_VideoId",
                table: "Media",
                column: "VideoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YtDlpImportFailures_VideoId",
                table: "YtDlpImportFailures",
                column: "VideoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YtDlpImportFailures");

            migrationBuilder.DropIndex(
                name: "IX_Media_VideoId",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "UploadDate",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "VideoId",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "YtDlpMediaKind",
                table: "Media");
        }
    }
}
