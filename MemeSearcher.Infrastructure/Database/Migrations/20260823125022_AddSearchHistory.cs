using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    IsComposite = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScopeDescription = table.Column<string>(type: "TEXT", nullable: false),
                    ResultCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_SearchedAt",
                table: "SearchHistory",
                column: "SearchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchHistory");
        }
    }
}
