using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneNGramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneNGramPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NGram = table.Column<string>(type: "TEXT", nullable: false),
                    StreamPosition = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneNGramPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneNGramPostings_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneNGramPostings_MediaId_NGram_StreamPosition",
                table: "PhoneNGramPostings",
                columns: new[] { "MediaId", "NGram", "StreamPosition" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneNGramPostings");
        }
    }
}
