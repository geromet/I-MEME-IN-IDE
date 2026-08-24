using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateIdToSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "QueryText",
                table: "SearchHistory",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "Language",
                table: "SearchHistory",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "SearchHistory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateName",
                table: "SearchHistory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_TemplateId",
                table: "SearchHistory",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_SearchHistory_Templates_TemplateId",
                table: "SearchHistory",
                column: "TemplateId",
                principalTable: "Templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SearchHistory_Templates_TemplateId",
                table: "SearchHistory");

            migrationBuilder.DropIndex(
                name: "IX_SearchHistory_TemplateId",
                table: "SearchHistory");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "SearchHistory");

            migrationBuilder.DropColumn(
                name: "TemplateName",
                table: "SearchHistory");

            migrationBuilder.AlterColumn<string>(
                name: "QueryText",
                table: "SearchHistory",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Language",
                table: "SearchHistory",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
