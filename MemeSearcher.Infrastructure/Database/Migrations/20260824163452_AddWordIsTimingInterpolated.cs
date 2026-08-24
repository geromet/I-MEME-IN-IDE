using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWordIsTimingInterpolated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTimingInterpolated",
                table: "Words",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTimingInterpolated",
                table: "Words");
        }
    }
}
