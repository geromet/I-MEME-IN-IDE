using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneAlphabetTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing Word was phonemized by EspeakPhonemizer, which is invoked with --ipa.
            migrationBuilder.AddColumn<string>(
                name: "PhonemeAlphabet",
                table: "Words",
                type: "TEXT",
                nullable: false,
                defaultValue: "Ipa");

            migrationBuilder.AddColumn<string>(
                name: "Alphabet",
                table: "Phones",
                type: "TEXT",
                nullable: false,
                defaultValue: "Ipa");

            // Backfill, rather than leave existing rows on the column default. Phone rows have
            // exactly one writer - MediaIngestionService.RealignAsync - fed by IAlignmentProvider,
            // and the only provider that has ever produced phones is MFA, whose english_us_arpa
            // models emit ARPABET. WhisperXAlignmentProvider is word-level and produces none. So
            // every pre-existing Phone row is ARPABET, and defaulting it to Ipa would mis-tag the
            // whole table - converting ARPABET symbols as if they were already canonical, which is
            // precisely the silent corruption this change exists to prevent (#18).
            migrationBuilder.Sql("UPDATE Phones SET Alphabet = 'Arpabet';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhonemeAlphabet",
                table: "Words");

            migrationBuilder.DropColumn(
                name: "Alphabet",
                table: "Phones");
        }
    }
}
