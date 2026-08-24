using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    SearchOptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TargetCatalogId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Templates_Catalogs_TargetCatalogId",
                        column: x => x.TargetCatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TemplateVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    PhonesRaw = table.Column<string>(type: "TEXT", nullable: false),
                    Alphabet = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateVariants_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_TargetCatalogId",
                table: "Templates",
                column: "TargetCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVariants_TemplateId",
                table: "TemplateVariants",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemplateVariants");

            migrationBuilder.DropTable(
                name: "Templates");
        }
    }
}
